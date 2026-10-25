FROM golang:1.27.3-bookworm AS build
WORKDIR /src
COPY go.mod main.go main_test.go ./
RUN go test -v ./...
RUN CGO_ENABLED=0 go build -trimpath -o /app/api .
FROM debian:bookworm-slim
WORKDIR /app
COPY --from=build /app/api /app/api
ENV GOMAXPROCS=1
ENTRYPOINT ["/app/api"]
