use axum::{extract::DefaultBodyLimit, http::StatusCode, routing::{get, post}, Json, Router};
use serde::{Deserialize, Serialize};
use std::collections::BTreeMap;

#[derive(Deserialize, Serialize)]
#[serde(deny_unknown_fields)]
struct Payload {
    id: i32,
    name: String,
    active: bool,
    score: f64,
    text: String,
    tags: Vec<String>,
    items: Vec<Item>,
    attributes: BTreeMap<String, String>,
}

#[derive(Deserialize, Serialize)]
#[serde(deny_unknown_fields)]
struct Item {
    id: i32,
    price: f64,
    quantity: i32,
}

async fn parse(text: String) -> Result<Json<Payload>, StatusCode> {
    serde_json::from_str::<Payload>(&text)
        .map(Json)
        .map_err(|_| StatusCode::BAD_REQUEST)
}

#[tokio::main(flavor = "multi_thread", worker_threads = 1)]
async fn main() {
    let app = Router::new()
        .route("/health", get(|| async { "ok" }))
        .route("/parse", post(parse))
        .layer(DefaultBodyLimit::max(1_048_576));
    let listener = tokio::net::TcpListener::bind("0.0.0.0:8080").await.expect("bind");
    axum::serve(listener, app).await.expect("serve");
}
