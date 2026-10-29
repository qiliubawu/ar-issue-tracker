// ============================================================================
//  ARIssueReporter.cs —— 园区 AR 巡检 · Unity 移动端
//
//  设计目标：把脚本挂到 XR Origin 上就能跑，不需要手动搭 UI、不需要做预制体。
//  运行后会自动：
//    1. 补齐 AR Plane Manager / AR Raycast Manager（只检测水平面）
//    2. 在屏幕底部生成整套中文操作面板（后端地址 / 标题 / 描述 / 优先级 / 提交）
//    3. 点击水平面放置红色标记球，实时显示 (x, y, z) 世界坐标
//    4. 点击 UI 时不会误触发 AR 放置（防误触）
//    5. 点“提交问题”把 JSON POST 到 Go 后端
//
//  适配：Unity 2022.3 LTS / Unity 6，AR Foundation 5.x，ARCore / ARKit
//  输入：自动兼容「旧输入系统 Input Manager」与「新输入系统 Input System Package」，
//        不需要修改 Project Settings -> Active Input Handling，也不需要重启编辑器。
//  联网：打真机包时 Project Settings -> Player -> Allow downloads over HTTP 需设为 Always allowed
// ============================================================================

using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Networking;
using UnityEngine.UI;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

// ---------------------------- 与后端对齐的数据结构 ----------------------------

[Serializable]
public class PositionData
{
    public float x;
    public float y;
    public float z;
}

[Serializable]
public class IssueCreateRequest
{
    public string title;
    public string description;
    public string priority;
    public PositionData position;
}

[Serializable]
public class IssueResponse
{
    public string id;
    public string title;
    public string status;
}

// ------------------------------- 主逻辑 -------------------------------------

public class ARIssueReporter : MonoBehaviour
{
    [Header("AR 组件（留空则自动查找/自动添加）")]
    [Tooltip("用于水平面射线检测，留空自动获取")]
    public ARRaycastManager raycastManager;

    [Tooltip("用于开启水平面检测，留空自动获取")]
    public ARPlaneManager planeManager;

    [Header("后端地址")]
    [Tooltip("真机测试时改成电脑的局域网 IP；编辑器里用 http://localhost:8080")]
    public string serverUrl = "http://192.168.10.11:8080";

    [Header("标记外观")]
    public float markerScale = 0.1f;
    public Color markerColor = new Color(0.9f, 0.15f, 0.15f, 1f);

    [Header("调试")]
    [Tooltip("没有识别到平面时，是否允许放在相机前方 1.5 米处（仅便于桌面调试）")]
    public bool allowPlaceWithoutPlane = false;

    // ---- 运行时状态 ----
    private GameObject currentMarker;
    private bool hasMarker;
    private Vector3 markerPosition;
    private static readonly List<ARRaycastHit> Hits = new List<ARRaycastHit>();
    private readonly List<RaycastResult> uiRaycastResults = new List<RaycastResult>();
    private Camera arCamera;

    // ---- UI 引用 ----
    private InputField serverInput;
    private InputField titleInput;
    private InputField descInput;
    private Text coordText;
    private Text statusText;
    private Button submitButton;
    private readonly Button[] priorityButtons = new Button[3];
    private readonly Image[] priorityButtonImages = new Image[3];
    private static readonly string[] Priorities = { "low", "medium", "high" };
    private static readonly string[] PriorityLabels = { "低", "中", "高" };
    private int priorityIndex = 1; // 默认 medium

    private bool submitting;

    // ======================== 生命周期 ========================

    private void Awake()
    {
        EnsureARComponents();
        arCamera = GetComponentInChildren<Camera>();
        if (arCamera == null) arCamera = Camera.main;
    }

    private void Start()
    {
        BuildUI();
        UpdateStatus("移动手机扫描地面，点击水平面放置标记", new Color(0.85f, 0.88f, 0.92f));
        SetCoordText(Vector3.zero, false);
    }

    private void OnEnable()
    {
        // 使用新输入系统时，触摸信息需要先启用 EnhancedTouch
#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
        UnityEngine.InputSystem.EnhancedTouch.EnhancedTouchSupport.Enable();
#endif
    }

    private void OnDisable()
    {
#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
        UnityEngine.InputSystem.EnhancedTouch.EnhancedTouchSupport.Disable();
#endif
    }

    private void Update()
    {
        if (submitting) return;

        if (!TryGetTapPosition(out Vector2 screenPosition)) return;

        if (IsPointerOverUI(screenPosition)) return; // 防误触：点在 UI 上不放标记
        TryPlaceMarker(screenPosition);
    }

    /// <summary>
    /// 取出“本帧按下的那一次点击”的屏幕坐标。
    /// 同时兼容旧输入系统（Input Manager）与新输入系统（Input System Package），
    /// 所以不需要改 Active Input Handling 设置。
    /// </summary>
    private bool TryGetTapPosition(out Vector2 position)
    {
        position = Vector2.zero;

#if ENABLE_LEGACY_INPUT_MANAGER
        // ---- 旧输入系统：真机触摸 + 编辑器鼠标 ----
        if (Input.touchCount > 0)
        {
            Touch touch = Input.GetTouch(0);
            if (touch.phase != TouchPhase.Began) return false;
            position = touch.position;
            return true;
        }
        if (Input.GetMouseButtonDown(0))
        {
            position = Input.mousePosition;
            return true;
        }
        return false;

#elif ENABLE_INPUT_SYSTEM
        // ---- 新输入系统：真机触摸 + 编辑器鼠标 ----
        var activeTouches = UnityEngine.InputSystem.EnhancedTouch.Touch.activeTouches;
        for (int i = 0; i < activeTouches.Count; i++)
        {
            if (activeTouches[i].phase == UnityEngine.InputSystem.TouchPhase.Began)
            {
                position = activeTouches[i].screenPosition;
                return true;
            }
        }

        var mouse = UnityEngine.InputSystem.Mouse.current;
        if (mouse != null && mouse.leftButton.wasPressedThisFrame)
        {
            position = mouse.position.ReadValue();
            return true;
        }
        return false;

#else
        return false;
#endif
    }

    // ======================== AR 组件准备 ========================

    private void EnsureARComponents()
    {
        if (raycastManager == null) raycastManager = GetComponent<ARRaycastManager>();
        if (raycastManager == null) raycastManager = FindFirst<ARRaycastManager>();
        if (raycastManager == null)
        {
            raycastManager = gameObject.AddComponent<ARRaycastManager>();
            Debug.Log("[ARIssueReporter] 已自动添加 AR Raycast Manager");
        }

        if (planeManager == null) planeManager = GetComponent<ARPlaneManager>();
        if (planeManager == null) planeManager = FindFirst<ARPlaneManager>();
        if (planeManager == null)
        {
            planeManager = gameObject.AddComponent<ARPlaneManager>();
            Debug.Log("[ARIssueReporter] 已自动添加 AR Plane Manager");
        }

        // 题目只要求识别水平面
        planeManager.detectionMode = PlaneDetectionMode.Horizontal;
    }

    /// <summary>兼容 Unity 2022（FindObjectOfType）与 Unity 6（FindFirstObjectByType）</summary>
    private static T FindFirst<T>() where T : UnityEngine.Object
    {
#if UNITY_2023_1_OR_NEWER
        return FindFirstObjectByType<T>();
#else
        return FindObjectOfType<T>();
#endif
    }

    // ======================== 防误触 ========================

    /// <summary>
    /// 点击位置是否落在任意 UI 上（防误触核心）。
    /// 用 GraphicRaycaster 自己打一条射线，比 IsPointerOverGameObject 更可靠，
    /// 而且新旧输入系统通用（不依赖 fingerId 映射）。
    /// </summary>
    private bool IsPointerOverUI(Vector2 screenPosition)
    {
        if (EventSystem.current == null) return false;

        // 快速判断：鼠标/单指场景下这行通常就够
        if (EventSystem.current.IsPointerOverGameObject()) return true;

        // 权威判断：对屏幕坐标做 UI 射线检测
        var data = new PointerEventData(EventSystem.current) { position = screenPosition };
        uiRaycastResults.Clear();
        EventSystem.current.RaycastAll(data, uiRaycastResults);
        return uiRaycastResults.Count > 0;
    }

    // ======================== 放置标记 ========================

    private void TryPlaceMarker(Vector2 screenPosition)
    {
        if (raycastManager != null &&
            raycastManager.Raycast(screenPosition, Hits, TrackableType.PlaneWithinPolygon))
        {
            PlaceMarkerAt(Hits[0].pose.position, Hits[0].pose.rotation);
            return;
        }

        if (allowPlaceWithoutPlane && arCamera != null)
        {
            Vector3 p = arCamera.transform.position + arCamera.transform.forward * 1.5f;
            PlaceMarkerAt(p, Quaternion.identity);
            UpdateStatus("未识别到平面，已按相机前方 1.5m 放置（调试模式）", new Color(1f, 0.85f, 0.4f));
            return;
        }

        UpdateStatus("还没识别到水平面：请缓慢移动手机扫描地面后再点击", new Color(1f, 0.75f, 0.3f));
    }

    private void PlaceMarkerAt(Vector3 position, Quaternion rotation)
    {
        markerPosition = position;
        hasMarker = true;

        if (currentMarker == null)
        {
            currentMarker = CreateMarker();
        }

        currentMarker.transform.SetPositionAndRotation(position, rotation);
        currentMarker.SetActive(true);

        SetCoordText(position, true);
        UpdateStatus("已放置标记，填写问题后点击“提交问题”", new Color(0.45f, 0.85f, 1f));
    }

    private GameObject CreateMarker()
    {
        GameObject go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        go.name = "AR_Issue_Marker";
        go.transform.localScale = Vector3.one * markerScale;

        // 去掉球体自带的碰撞体，避免挡住后续射线
        Collider col = go.GetComponent<Collider>();
        if (col != null) Destroy(col);

        Renderer renderer = go.GetComponent<Renderer>();
        if (renderer != null) renderer.material = CreateMarkerMaterial();

        return go;
    }

    private Material CreateMarkerMaterial()
    {
        // 依次尝试 URP / 内置管线 / 无光照，保证任何模板下都是红色
        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null) shader = Shader.Find("Standard");
        if (shader == null) shader = Shader.Find("Unlit/Color");
        if (shader == null) shader = Shader.Find("Sprites/Default");

        Material mat = new Material(shader);
        if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", markerColor);
        if (mat.HasProperty("_Color")) mat.SetColor("_Color", markerColor);
        return mat;
    }

    // ======================== 提交 ========================

    private void OnSubmitClicked()
    {
        if (submitting) return;

        if (!hasMarker)
        {
            UpdateStatus("提交失败：请先点击识别到的水平面放置 AR 标记", new Color(1f, 0.45f, 0.45f));
            return;
        }

        string title = titleInput != null ? titleInput.text.Trim() : string.Empty;
        if (string.IsNullOrEmpty(title))
        {
            UpdateStatus("提交失败：问题标题不能为空", new Color(1f, 0.45f, 0.45f));
            return;
        }

        string baseUrl = serverInput != null ? serverInput.text.Trim().TrimEnd('/') : serverUrl.TrimEnd('/');
        if (string.IsNullOrEmpty(baseUrl))
        {
            UpdateStatus("提交失败：后端地址不能为空", new Color(1f, 0.45f, 0.45f));
            return;
        }

        IssueCreateRequest payload = new IssueCreateRequest
        {
            title = title,
            description = descInput != null ? descInput.text.Trim() : string.Empty,
            priority = Priorities[Mathf.Clamp(priorityIndex, 0, 2)],
            position = new PositionData
            {
                x = (float)Math.Round(markerPosition.x, 2),
                y = (float)Math.Round(markerPosition.y, 2),
                z = (float)Math.Round(markerPosition.z, 2)
            }
        };

        StartCoroutine(PostIssue(baseUrl + "/api/issues", JsonUtility.ToJson(payload)));
    }

    private IEnumerator PostIssue(string url, string json)
    {
        submitting = true;
        if (submitButton != null) submitButton.interactable = false;
        UpdateStatus("正在提交…", new Color(1f, 0.85f, 0.4f));

        using (UnityWebRequest req = new UnityWebRequest(url, UnityWebRequest.kHttpVerbPOST))
        {
            byte[] body = Encoding.UTF8.GetBytes(json);
            req.uploadHandler = new UploadHandlerRaw(body);
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");
            req.timeout = 8;

            yield return req.SendWebRequest();

            bool ok = req.result == UnityWebRequest.Result.Success &&
                      req.responseCode >= 200 && req.responseCode < 300;

            if (ok)
            {
                string id = "-";
                try
                {
                    IssueResponse resp = JsonUtility.FromJson<IssueResponse>(req.downloadHandler.text);
                    if (resp != null && !string.IsNullOrEmpty(resp.id)) id = resp.id;
                }
                catch { /* 解析失败不影响提交结果 */ }

                UpdateStatus("提交成功！ID: " + id + "（可在电脑 React 页面刷新查看）",
                    new Color(0.45f, 0.9f, 0.5f));
                if (titleInput != null) titleInput.text = string.Empty;
                if (descInput != null) descInput.text = string.Empty;
            }
            else
            {
                string detail = req.downloadHandler != null ? req.downloadHandler.text : string.Empty;
                if (string.IsNullOrEmpty(detail)) detail = req.error;
                UpdateStatus("提交失败 (" + req.responseCode + ")：" + detail, new Color(1f, 0.45f, 0.45f));
                Debug.LogWarning("[ARIssueReporter] 提交失败: " + detail);
            }
        }

        submitting = false;
        if (submitButton != null) submitButton.interactable = true;
    }

    private void SetCoordText(Vector3 p, bool valid)
    {
        if (coordText == null) return;
        coordText.text = valid
            ? string.Format("当前标记坐标: x={0:F2}  y={1:F2}  z={2:F2}", p.x, p.y, p.z)
            : "当前标记坐标: 尚未放置";
    }

    private void UpdateStatus(string message, Color color)
    {
        if (statusText == null) return;
        statusText.text = message;
        statusText.color = color;
    }

    // ======================== 自动生成 UI ========================

    private void BuildUI()
    {
        EnsureEventSystem();

        Font font = LoadFont();

        // --- Canvas ---
        GameObject canvasGo = new GameObject("ARIssueCanvas",
            typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasGo.transform.SetParent(transform, false);

        Canvas canvas = canvasGo.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 100;

        CanvasScaler scaler = canvasGo.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1080, 1920);
        scaler.matchWidthOrHeight = 0.5f;

        // --- 底部面板 ---
        GameObject panel = CreateRect("Panel", canvasGo.transform);
        RectTransform panelRect = panel.GetComponent<RectTransform>();
        panelRect.anchorMin = new Vector2(0f, 0f);
        panelRect.anchorMax = new Vector2(1f, 0f);
        panelRect.pivot = new Vector2(0.5f, 0f);
        panelRect.anchoredPosition = new Vector2(0f, 0f);
        panelRect.sizeDelta = new Vector2(0f, 520f);

        Image panelBg = panel.AddComponent<Image>();
        panelBg.color = new Color(0.06f, 0.08f, 0.11f, 0.82f);
        panelBg.raycastTarget = true; // 面板本身也要挡住射线，防止穿透到 AR

        VerticalLayoutGroup vlg = panel.AddComponent<VerticalLayoutGroup>();
        vlg.padding = new RectOffset(36, 36, 24, 24);
        vlg.spacing = 14;
        vlg.childAlignment = TextAnchor.UpperCenter;
        vlg.childControlWidth = true;
        vlg.childControlHeight = true;
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;

        // 状态提示
        statusText = CreateText(panel.transform, font, 28, FontStyle.Bold,
            new Color(0.85f, 0.88f, 0.92f), "移动手机扫描地面，点击水平面放置标记");
        statusText.gameObject.AddComponent<LayoutElement>().preferredHeight = 62f;

        // 坐标
        coordText = CreateText(panel.transform, font, 26, FontStyle.Normal,
            new Color(0.7f, 0.78f, 0.88f), "当前标记坐标: 尚未放置");
        coordText.gameObject.AddComponent<LayoutElement>().preferredHeight = 42f;

        // 后端地址
        serverInput = CreateInputField(panel.transform, font, "Go后端地址", serverUrl, 72f);

        // 标题
        titleInput = CreateInputField(panel.transform, font, "问题标题（必填）", "例如：入口墙面破损", 72f);

        // 描述
        descInput = CreateInputField(panel.transform, font, "问题描述（可选）", "例如：左侧墙体存在裂缝", 72f);

        // 优先级
        GameObject priorityRow = CreateRect("PriorityRow", panel.transform);
        priorityRow.AddComponent<LayoutElement>().preferredHeight = 68f;
        HorizontalLayoutGroup hlg = priorityRow.AddComponent<HorizontalLayoutGroup>();
        hlg.spacing = 12;
        hlg.childControlWidth = true;
        hlg.childControlHeight = true;
        hlg.childForceExpandWidth = true;
        hlg.childForceExpandHeight = true;

        Text priorityLabel = CreateText(priorityRow.transform, font, 26, FontStyle.Normal,
            new Color(0.72f, 0.78f, 0.86f), "优先级");
        priorityLabel.gameObject.AddComponent<LayoutElement>().preferredWidth = 110f;

        for (int i = 0; i < 3; i++)
        {
            int index = i;
            Button b = CreateButton(priorityRow.transform, font, PriorityLabels[i], 26,
                out Image img);
            b.onClick.AddListener(() => SelectPriority(index));
            priorityButtons[i] = b;
            priorityButtonImages[i] = img;
        }

        // 提交按钮
        Button submit = CreateButton(panel.transform, font, "提交问题", 30, out Image submitImg);
        submitImg.color = new Color(0.16f, 0.42f, 0.92f, 1f);
        submit.gameObject.AddComponent<LayoutElement>().preferredHeight = 88f;
        submit.onClick.AddListener(OnSubmitClicked);
        submitButton = submit;

        SelectPriority(priorityIndex);
    }

    private void SelectPriority(int index)
    {
        priorityIndex = index;
        for (int i = 0; i < priorityButtonImages.Length; i++)
        {
            if (priorityButtonImages[i] == null) continue;
            priorityButtonImages[i].color = i == priorityIndex
                ? new Color(0.16f, 0.42f, 0.92f, 1f)
                : new Color(1f, 1f, 1f, 0.14f);
        }
    }

    /// <summary>场景里没有 EventSystem 时补一个，并按当前输入系统选择正确的 UI 模块</summary>
    private static void EnsureEventSystem()
    {
        if (FindFirst<EventSystem>() != null) return;

        GameObject es = new GameObject("EventSystem");
        es.AddComponent<EventSystem>();
#if ENABLE_LEGACY_INPUT_MANAGER
        es.AddComponent<StandaloneInputModule>();
#elif ENABLE_INPUT_SYSTEM
        es.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
#else
        es.AddComponent<StandaloneInputModule>();
#endif
        Debug.Log("[ARIssueReporter] 场景中没有 EventSystem，已自动创建");
    }

    // ---------- UI 构造小工具 ----------

    private static GameObject CreateRect(string name, Transform parent)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return go;
    }

    private static Text CreateText(Transform parent, Font font, int size, FontStyle style,
        Color color, string content)
    {
        GameObject go = new GameObject("Text", typeof(RectTransform));
        go.transform.SetParent(parent, false);

        Text t = go.AddComponent<Text>();
        t.font = font;
        t.fontSize = size;
        t.fontStyle = style;
        t.color = color;
        t.text = content;
        t.alignment = TextAnchor.MiddleLeft;
        t.horizontalOverflow = HorizontalWrapMode.Wrap;
        t.verticalOverflow = VerticalWrapMode.Overflow;
        t.raycastTarget = false; // 文本不拦截点击
        return t;
    }

    private static InputField CreateInputField(Transform parent, Font font, string placeholder, string value, float height)
    {
        GameObject go = new GameObject("Input_" + placeholder, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        go.AddComponent<LayoutElement>().preferredHeight = height;

        Image bg = go.AddComponent<Image>();
        bg.color = new Color(1f, 1f, 1f, 0.12f);

        GameObject textGo = new GameObject("Text", typeof(RectTransform));
        textGo.transform.SetParent(go.transform, false);
        Text text = textGo.AddComponent<Text>();
        text.font = font;
        text.fontSize = 28;
        text.color = Color.white;
        text.alignment = TextAnchor.MiddleLeft;
        text.supportRichText = false;
        text.raycastTarget = false;

        GameObject phGo = new GameObject("Placeholder", typeof(RectTransform));
        phGo.transform.SetParent(go.transform, false);
        Text ph = phGo.AddComponent<Text>();
        ph.font = font;
        ph.fontSize = 28;
        ph.color = new Color(1f, 1f, 1f, 0.38f);
        ph.text = placeholder;
        ph.alignment = TextAnchor.MiddleLeft;
        ph.supportRichText = false;
        ph.raycastTarget = false;

        InputField input = go.AddComponent<InputField>();
        input.targetGraphic = bg;
        input.textComponent = text;
        input.placeholder = ph;
        input.lineType = InputField.LineType.SingleLine;
        input.text = value;

        RectTransform textRect = textGo.GetComponent<RectTransform>();
        textRect.anchorMin = new Vector2(0f, 0f);
        textRect.anchorMax = new Vector2(1f, 1f);
        textRect.offsetMin = new Vector2(18f, 6f);
        textRect.offsetMax = new Vector2(-18f, -6f);

        RectTransform phRect = phGo.GetComponent<RectTransform>();
        phRect.anchorMin = new Vector2(0f, 0f);
        phRect.anchorMax = new Vector2(1f, 1f);
        phRect.offsetMin = new Vector2(18f, 6f);
        phRect.offsetMax = new Vector2(-18f, -6f);

        return input;
    }

    private static Button CreateButton(Transform parent, Font font, string label, int fontSize, out Image image)
    {
        GameObject go = new GameObject("Btn_" + label, typeof(RectTransform));
        go.transform.SetParent(parent, false);

        image = go.AddComponent<Image>();
        image.color = new Color(1f, 1f, 1f, 0.14f);

        Button button = go.AddComponent<Button>();
        button.targetGraphic = image;

        Text text = CreateText(go.transform, font, fontSize, FontStyle.Bold, Color.white, label);
        text.alignment = TextAnchor.MiddleCenter;

        RectTransform textRect = text.gameObject.GetComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = Vector2.zero;
        textRect.offsetMax = Vector2.zero;

        return button;
    }

    /// <summary>优先用系统里带中文字形的字体，避免界面出现方块</summary>
    private static Font LoadFont()
    {
        string[] candidates =
        {
            "Microsoft YaHei", "微软雅黑", "PingFang SC", "Heiti SC",
            "Noto Sans CJK SC", "Source Han Sans CN", "Droid Sans Fallback", "Arial"
        };

        try
        {
            Font os = Font.CreateDynamicFontFromOSFont(candidates, 28);
            if (os != null) return os;
        }
        catch { /* 某些平台不支持按名字取系统字体 */ }

        try
        {
            Font builtin = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (builtin != null) return builtin;
        }
        catch { /* Unity 2022.1 及更早叫 Arial.ttf */ }

        try
        {
            Font legacy = Resources.GetBuiltinResource<Font>("Arial.ttf");
            if (legacy != null) return legacy;
        }
        catch { /* ignore */ }

        Debug.LogError("[ARIssueReporter] 未找到可用字体，UI 文字可能不显示");
        return null;
    }
}
