package main

import (
	"encoding/json"
	"net/http/httptest"
	"reflect"
	"strings"
	"testing"
)

const validBody = `{"id":1,"name":"test","active":false,"score":1.25,"text":"hello","tags":["a"],"items":[{"id":2,"price":3.25,"quantity":1}],"attributes":{"key":"value"}}`

func TestParse(t *testing.T) {
	tests := []struct {
		name   string
		body   string
		status int
	}{
		{"valid", validBody, 200},
		{"case insensitive", strings.Replace(validBody, `"name"`, `"Name"`, 1), 200},
		{"unicode", strings.Replace(validBody, "test", "Chào bạn 東京 😀", 1), 200},
		{"empty", "", 400}, {"malformed", "{", 400}, {"root null", "null", 400},
		{"root array", "[]", 400}, {"missing", "{}", 400},
		{"unknown", strings.Replace(validBody, `"id":1`, `"id":1,"extra":1`, 1), 400},
		{"wrong type", strings.Replace(validBody, `"id":1`, `"id":"bad"`, 1), 400},
		{"overflow", strings.Replace(validBody, `"id":1`, `"id":2147483648`, 1), 400},
		{"null name", strings.Replace(validBody, `"name":"test"`, `"name":null`, 1), 400},
		{"null active", strings.Replace(validBody, `"active":false`, `"active":null`, 1), 400},
		{"null tags", strings.Replace(validBody, `"tags":["a"]`, `"tags":null`, 1), 400},
		{"null tag", strings.Replace(validBody, `["a"]`, `[null]`, 1), 400},
		{"null item", strings.Replace(validBody, `[{"id":2,"price":3.25,"quantity":1}]`, `[null]`, 1), 400},
		{"null attribute", strings.Replace(validBody, `"value"`, `null`, 1), 400},
		{"missing nested", strings.Replace(validBody, `,"quantity":1`, "", 1), 400},
		{"extra nested", strings.Replace(validBody, `"quantity":1`, `"quantity":1,"extra":0`, 1), 400},
		{"trailing", validBody + " {}", 400},
		{"invalid UTF-8", string([]byte{0xff, 0xfe}), 400},
		{"too large", strings.Repeat(" ", 1_048_577), 413},
	}
	handler := routes()
	for _, test := range tests {
		t.Run(test.name, func(t *testing.T) {
			request := httptest.NewRequest("POST", "/parse", strings.NewReader(test.body))
			request.Header.Set("Content-Type", "text/plain; charset=utf-8")
			response := httptest.NewRecorder()
			handler.ServeHTTP(response, request)
			if response.Code != test.status {
				t.Fatalf("status %d want %d: %s", response.Code, test.status, response.Body.String())
			}
			if test.status == 200 {
				var expected, actual any
				if err := json.Unmarshal([]byte(test.body), &expected); err != nil {
					t.Fatal(err)
				}
				if test.name == "case insensitive" {
					expected.(map[string]any)["name"] = expected.(map[string]any)["Name"]
					delete(expected.(map[string]any), "Name")
				}
				if err := json.Unmarshal(response.Body.Bytes(), &actual); err != nil {
					t.Fatal(err)
				}
				if !reflect.DeepEqual(expected, actual) {
					t.Fatalf("roundtrip mismatch")
				}
			}
		})
	}
}
