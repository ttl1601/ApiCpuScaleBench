FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY DbReadBench.csproj .
RUN dotnet restore
COPY *.cs ./
RUN dotnet publish -c Release -o /app --no-restore /p:UseAppHost=false
FROM mcr.microsoft.com/dotnet/runtime:10.0
WORKDIR /app
COPY --from=build /app .
ENTRYPOINT ["dotnet", "DbReadBench.dll"]
