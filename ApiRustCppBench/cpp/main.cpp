#include <drogon/drogon.h>
#include <nlohmann/json.hpp>
#include <cmath>
#include <cstdint>
#include <limits>
#include <map>
#include <stdexcept>
#include <string>
#include <vector>
using BenchJson = nlohmann::json;
struct Item { std::int32_t id; double price; std::int32_t quantity; };
struct Payload {
    std::int32_t id; std::string name; bool active; double score; std::string text;
    std::vector<std::string> tags; std::vector<Item> items; std::map<std::string, std::string> attributes;
};
void require(bool valid) { if (!valid) throw std::invalid_argument("Invalid payload"); }
void fields(const BenchJson& value, std::initializer_list<const char*> names) {
    require(value.is_object() && value.size() == names.size());
    for (const auto* name : names) require(value.contains(name));
}
std::int32_t integer(const BenchJson& value) {
    require(value.is_number_integer());
    if (value.is_number_unsigned()) require(value.get<std::uint64_t>() <= INT32_MAX);
    else require(value.get<std::int64_t>() >= INT32_MIN && value.get<std::int64_t>() <= INT32_MAX);
    return value.get<std::int32_t>();
}
double number(const BenchJson& value) {
    require(value.is_number());
    double result = value.get<double>(); require(std::isfinite(result)); return result;
}
void from_json(const BenchJson& value, Item& item) {
    fields(value, {"id", "price", "quantity"});
    item.id = integer(value.at("id")); item.price = number(value.at("price")); item.quantity = integer(value.at("quantity"));
}
void to_json(BenchJson& value, const Item& item) { value = BenchJson{{"id", item.id}, {"price", item.price}, {"quantity", item.quantity}}; }
void from_json(const BenchJson& value, Payload& payload) {
    fields(value, {"id", "name", "active", "score", "text", "tags", "items", "attributes"});
    payload.id = integer(value.at("id")); payload.score = number(value.at("score"));
    require(value.at("name").is_string() && value.at("text").is_string() && value.at("active").is_boolean());
    value.at("name").get_to(payload.name); value.at("text").get_to(payload.text); value.at("active").get_to(payload.active);
    require(value.at("tags").is_array() && value.at("items").is_array() && value.at("attributes").is_object());
    value.at("tags").get_to(payload.tags); value.at("items").get_to(payload.items); value.at("attributes").get_to(payload.attributes);
}
void to_json(BenchJson& value, const Payload& payload) {
    value = BenchJson{{"id", payload.id}, {"name", payload.name}, {"active", payload.active}, {"score", payload.score},
        {"text", payload.text}, {"tags", payload.tags}, {"items", payload.items}, {"attributes", payload.attributes}};
}
int main() {
    drogon::app().setLogLevel(trantor::Logger::kError).setThreadNum(1).setClientMaxBodySize(1048576).setClientMaxMemoryBodySize(1048576);
    drogon::app().registerHandler("/health", [](const drogon::HttpRequestPtr&, std::function<void(const drogon::HttpResponsePtr&)>&& callback) {
        auto response = drogon::HttpResponse::newHttpResponse(); response->setBody("ok"); callback(response);
    }, {drogon::Get});
    drogon::app().registerHandler("/parse", [](const drogon::HttpRequestPtr& request, std::function<void(const drogon::HttpResponsePtr&)>&& callback) {
        auto response = drogon::HttpResponse::newHttpResponse();
        try {
            std::string text(request->body());
            require(!text.starts_with("\xEF\xBB\xBF"));
            auto payload = BenchJson::parse(text).get<Payload>();
            response->setContentTypeCode(drogon::CT_APPLICATION_JSON);
            response->setBody(BenchJson(payload).dump());
        } catch (const std::exception&) { response->setStatusCode(drogon::k400BadRequest); }
        callback(response);
    }, {drogon::Post});
    drogon::app().addListener("0.0.0.0", 8080).run();
}
