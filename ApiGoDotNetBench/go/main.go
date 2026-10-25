package main

import (
	"context"
	"encoding/json"
	"errors"
	"io"
	"log"
	"net/http"
	"os"
	"os/signal"
	"runtime"
	"strings"
	"syscall"
	"time"
	"unicode/utf8"
)

type Payload struct {
	ID         *int32              `json:"id"`
	Name       *string             `json:"name"`
	Active     *bool               `json:"active"`
	Score      *float64            `json:"score"`
	Text       *string             `json:"text"`
	Tags       *[]*string          `json:"tags"`
	Items      *[]*Item            `json:"items"`
	Attributes *map[string]*string `json:"attributes"`
}

type Item struct {
	ID       *int32   `json:"id"`
	Price    *float64 `json:"price"`
	Quantity *int32   `json:"quantity"`
}

func (payload *Payload) valid() bool {
	if payload.ID == nil || payload.Name == nil || payload.Active == nil || payload.Score == nil ||
		payload.Text == nil || payload.Tags == nil || payload.Items == nil || payload.Attributes == nil {
		return false
	}
	for _, tag := range *payload.Tags {
		if tag == nil {
			return false
		}
	}
	for _, item := range *payload.Items {
		if item == nil || item.ID == nil || item.Price == nil || item.Quantity == nil {
			return false
		}
	}
	for _, value := range *payload.Attributes {
		if value == nil {
			return false
		}
	}
	return true
}

func handleParse(writer http.ResponseWriter, request *http.Request) {
	request.Body = http.MaxBytesReader(writer, request.Body, 1_048_576)
	data, err := io.ReadAll(request.Body)
	if err != nil {
		var sizeError *http.MaxBytesError
		if errors.As(err, &sizeError) {
			http.Error(writer, "body too large", http.StatusRequestEntityTooLarge)
		} else {
			http.Error(writer, "invalid body", http.StatusBadRequest)
		}
		return
	}
	text := string(data)
	if !utf8.ValidString(text) {
		http.Error(writer, "invalid UTF-8", http.StatusBadRequest)
		return
	}
	decoder := json.NewDecoder(strings.NewReader(text))
	decoder.DisallowUnknownFields()
	var payload Payload
	if err := decoder.Decode(&payload); err != nil || !payload.valid() {
		http.Error(writer, "invalid JSON object", http.StatusBadRequest)
		return
	}
	var extra any
	if err := decoder.Decode(&extra); err != io.EOF {
		http.Error(writer, "trailing JSON", http.StatusBadRequest)
		return
	}
	writer.Header().Set("Content-Type", "application/json")
	encoder := json.NewEncoder(writer)
	encoder.SetEscapeHTML(false)
	if err := encoder.Encode(&payload); err != nil {
		return
	}
}

func routes() http.Handler {
	mux := http.NewServeMux()
	mux.HandleFunc("GET /health", func(writer http.ResponseWriter, request *http.Request) {
		writer.Header().Set("Content-Type", "text/plain")
		_, _ = io.WriteString(writer, "ok")
	})
	mux.HandleFunc("POST /parse", handleParse)
	return mux
}

func main() {
	shutdownSignal, stop := signal.NotifyContext(context.Background(), os.Interrupt, syscall.SIGTERM)
	defer stop()
	server := &http.Server{Addr: ":8080", Handler: routes(), ReadHeaderTimeout: 10 * time.Second,
		ErrorLog: log.New(io.Discard, "", 0)}
	done := make(chan struct{})
	go func() {
		<-shutdownSignal.Done()
		shutdown, cancel := context.WithTimeout(context.Background(), 5*time.Second)
		defer cancel()
		_ = server.Shutdown(shutdown)
		close(done)
	}()
	log.Printf("runtime=%s GOMAXPROCS=%d", runtime.Version(), runtime.GOMAXPROCS(0))
	if err := server.ListenAndServe(); !errors.Is(err, http.ErrServerClosed) {
		log.Fatal(err)
	}
	<-done
}
