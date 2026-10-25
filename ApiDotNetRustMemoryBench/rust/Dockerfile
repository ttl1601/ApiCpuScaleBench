FROM rust:1.90-bookworm AS build
WORKDIR /src
COPY Cargo.toml Cargo.lock ./
COPY src ./src
RUN cargo build --release --locked
FROM debian:bookworm-slim
WORKDIR /app
COPY --from=build /src/target/release/json-api /app/json-api
ENTRYPOINT ["/app/json-api"]
