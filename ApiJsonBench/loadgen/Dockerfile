FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY Loadgen.csproj .
RUN dotnet restore
COPY *.cs ./
RUN dotnet publish -c Release --no-restore -o /app /p:UseAppHost=false
FROM mcr.microsoft.com/dotnet/runtime:10.0
WORKDIR /app
COPY --from=build /app .
ENTRYPOINT ["dotnet", "Loadgen.dll"]
