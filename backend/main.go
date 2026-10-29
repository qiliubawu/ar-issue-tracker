// 园区 AR 巡检 - Go 后端
// 纯标准库实现：net/http + 本地 issues.json 持久化，零第三方依赖。
// 运行：go run main.go   （默认监听 0.0.0.0:8080）
package main

import (
	"crypto/rand"
	"encoding/hex"
	"encoding/json"
	"errors"
	"fmt"
	"log"
	"net"
	"net/http"
	"os"
	"path/filepath"
	"strings"
	"sync"
	"time"
)

// ---------- 数据结构（三端字段名保持一致） ----------

type Position struct {
	X float64 `json:"x"`
	Y float64 `json:"y"`
	Z float64 `json:"z"`
}

type Issue struct {
	ID          string   `json:"id"`
	Title       string   `json:"title"`
	Description string   `json:"description"`
	Priority    string   `json:"priority"` // low | medium | high
	Status      string   `json:"status"`   // open | in_progress | resolved
	Position    Position `json:"position"`
	CreatedAt   string   `json:"createdAt"`
	UpdatedAt   string   `json:"updatedAt"`
}

// ---------- 持久化 ----------

var (
	mu       sync.RWMutex
	issues   = make([]Issue, 0)
	dataFile = "issues.json"
)

func loadData() error {
	mu.Lock()
	defer mu.Unlock()

	b, err := os.ReadFile(dataFile)
	if err != nil {
		if errors.Is(err, os.ErrNotExist) {
			issues = make([]Issue, 0)
			return nil
		}
		return err
	}
	if len(strings.TrimSpace(string(b))) == 0 {
		issues = make([]Issue, 0)
		return nil
	}
	if err := json.Unmarshal(b, &issues); err != nil {
		return fmt.Errorf("解析 %s 失败: %w", dataFile, err)
	}
	if issues == nil {
		issues = make([]Issue, 0)
	}
	return nil
}

// saveDataLocked 调用前必须已持有写锁
func saveDataLocked() error {
	b, err := json.MarshalIndent(issues, "", "  ")
	if err != nil {
		return err
	}
	// 先写临时文件再原子替换，避免写一半断电导致文件损坏
	tmp := dataFile + ".tmp"
	if err := os.WriteFile(tmp, b, 0o644); err != nil {
		return err
	}
	return os.Rename(tmp, dataFile)
}

func genID() string {
	b := make([]byte, 8)
	if _, err := rand.Read(b); err != nil {
		return fmt.Sprintf("%d", time.Now().UnixNano())
	}
	return hex.EncodeToString(b)
}

// ---------- 工具 ----------

func writeJSON(w http.ResponseWriter, code int, payload any) {
	w.Header().Set("Content-Type", "application/json; charset=utf-8")
	w.WriteHeader(code)
	_ = json.NewEncoder(w).Encode(payload)
}

func fail(w http.ResponseWriter, code int, msg string) {
	writeJSON(w, code, map[string]string{"error": msg})
}

func now() string { return time.Now().Format(time.RFC3339) }

// withCORS 允许浏览器端（Vite dev server）与真机跨域访问
func withCORS(next http.Handler) http.Handler {
	return http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		w.Header().Set("Access-Control-Allow-Origin", "*")
		w.Header().Set("Access-Control-Allow-Methods", "GET, POST, PATCH, PUT, OPTIONS")
		w.Header().Set("Access-Control-Allow-Headers", "Content-Type")
		w.Header().Set("Access-Control-Max-Age", "86400")
		if r.Method == http.MethodOptions {
			w.WriteHeader(http.StatusNoContent)
			return
		}
		log.Printf("%s %s", r.Method, r.URL.Path)
		next.ServeHTTP(w, r)
	})
}

// ---------- 业务处理 ----------

func handleHealth(w http.ResponseWriter, r *http.Request) {
	writeJSON(w, http.StatusOK, map[string]string{"status": "ok", "time": now()})
}

func handleList(w http.ResponseWriter, r *http.Request) {
	mu.RLock()
	defer mu.RUnlock()
	list := make([]Issue, len(issues))
	copy(list, issues)
	writeJSON(w, http.StatusOK, list)
}

func handleGetOne(w http.ResponseWriter, r *http.Request) {
	id := r.PathValue("id")
	mu.RLock()
	defer mu.RUnlock()
	for _, it := range issues {
		if it.ID == id {
			writeJSON(w, http.StatusOK, it)
			return
		}
	}
	fail(w, http.StatusNotFound, "未找到该问题 ID")
}

func handleCreate(w http.ResponseWriter, r *http.Request) {
	var req struct {
		Title       string   `json:"title"`
		Description string   `json:"description"`
		Priority    string   `json:"priority"`
		Position    Position `json:"position"`
	}
	dec := json.NewDecoder(http.MaxBytesReader(w, r.Body, 1<<20))
	dec.DisallowUnknownFields() // 字段名写错时给出明确报错，而不是静默丢弃
	if err := dec.Decode(&req); err != nil {
		fail(w, http.StatusBadRequest, "无效的 JSON 格式: "+err.Error())
		return
	}

	req.Title = strings.TrimSpace(req.Title)
	req.Description = strings.TrimSpace(req.Description)
	req.Priority = strings.ToLower(strings.TrimSpace(req.Priority))

	if req.Title == "" {
		fail(w, http.StatusBadRequest, "标题(title)不能为空")
		return
	}
	if len([]rune(req.Title)) > 120 {
		fail(w, http.StatusBadRequest, "标题(title)长度不能超过 120 个字符")
		return
	}
	if req.Priority != "low" && req.Priority != "medium" && req.Priority != "high" {
		fail(w, http.StatusBadRequest, "优先级(priority)必须为 low, medium 或 high")
		return
	}

	ts := now()
	item := Issue{
		ID:          genID(),
		Title:       req.Title,
		Description: req.Description,
		Priority:    req.Priority,
		Status:      "open",
		Position:    req.Position,
		CreatedAt:   ts,
		UpdatedAt:   ts,
	}

	mu.Lock()
	issues = append([]Issue{item}, issues...) // 最新的排在最前
	err := saveDataLocked()
	mu.Unlock()

	if err != nil {
		log.Printf("保存失败: %v", err)
		fail(w, http.StatusInternalServerError, "保存数据失败")
		return
	}
	writeJSON(w, http.StatusCreated, item)
}

func handleUpdateStatus(w http.ResponseWriter, r *http.Request) {
	id := r.PathValue("id")

	var req struct {
		Status string `json:"status"`
	}
	dec := json.NewDecoder(http.MaxBytesReader(w, r.Body, 1<<16))
	dec.DisallowUnknownFields()
	if err := dec.Decode(&req); err != nil {
		fail(w, http.StatusBadRequest, "无效的 JSON 格式: "+err.Error())
		return
	}

	status := strings.ToLower(strings.TrimSpace(req.Status))
	if status != "open" && status != "in_progress" && status != "resolved" {
		fail(w, http.StatusBadRequest, "状态(status)必须为 open, in_progress 或 resolved")
		return
	}

	mu.Lock()
	defer mu.Unlock()
	for i := range issues {
		if issues[i].ID == id {
			issues[i].Status = status
			issues[i].UpdatedAt = now()
			if err := saveDataLocked(); err != nil {
				log.Printf("保存失败: %v", err)
				fail(w, http.StatusInternalServerError, "保存数据失败")
				return
			}
			writeJSON(w, http.StatusOK, issues[i])
			return
		}
	}
	fail(w, http.StatusNotFound, "未找到该问题 ID")
}

// ---------- 启动 ----------

func lanIPs() []string {
	var out []string
	ifaces, err := net.Interfaces()
	if err != nil {
		return out
	}
	for _, ifi := range ifaces {
		if ifi.Flags&net.FlagUp == 0 || ifi.Flags&net.FlagLoopback != 0 {
			continue
		}
		addrs, err := ifi.Addrs()
		if err != nil {
			continue
		}
		for _, a := range addrs {
			if ipnet, ok := a.(*net.IPNet); ok && ipnet.IP.To4() != nil {
				out = append(out, ipnet.IP.String())
			}
		}
	}
	return out
}

func main() {
	if v := os.Getenv("ISSUE_DATA_FILE"); v != "" {
		dataFile = v
	}
	if abs, err := filepath.Abs(dataFile); err == nil {
		dataFile = abs
	}

	if err := loadData(); err != nil {
		log.Printf("加载历史数据失败（将以空列表启动）: %v", err)
	}

	port := os.Getenv("PORT")
	if port == "" {
		port = "8080"
	}

	mux := http.NewServeMux()
	mux.HandleFunc("GET /api/health", handleHealth)
	mux.HandleFunc("GET /api/issues", handleList)
	mux.HandleFunc("POST /api/issues", handleCreate)
	mux.HandleFunc("GET /api/issues/{id}", handleGetOne)
	mux.HandleFunc("PATCH /api/issues/{id}/status", handleUpdateStatus)
	mux.HandleFunc("PUT /api/issues/{id}/status", handleUpdateStatus)
	mux.HandleFunc("/", func(w http.ResponseWriter, r *http.Request) {
		fail(w, http.StatusNotFound, "路径不存在: "+r.URL.Path)
	})

	fmt.Println("====================================================")
	fmt.Println("Go 后端服务启动成功！")
	fmt.Printf("电脑端 React 默认连接地址: http://localhost:%s\n", port)
	fmt.Println("手机端 Unity 真机请填写以下局域网地址之一:")
	ips := lanIPs()
	if len(ips) == 0 {
		fmt.Println("  -> 未检测到局域网 IP，请确认已连接 Wi-Fi")
	}
	for _, ip := range ips {
		fmt.Printf("  -> http://%s:%s\n", ip, port)
	}
	fmt.Printf("数据文件: %s\n", dataFile)
	fmt.Println("====================================================")

	srv := &http.Server{
		Addr:              "0.0.0.0:" + port,
		Handler:           withCORS(mux),
		ReadHeaderTimeout: 10 * time.Second,
	}
	if err := srv.ListenAndServe(); err != nil && !errors.Is(err, http.ErrServerClosed) {
		log.Fatalf("服务启动失败: %v", err)
	}
}
