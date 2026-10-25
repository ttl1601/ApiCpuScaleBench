FROM mcr.microsoft.com/dotnet/sdk:11.0.100-rc.2-resolute AS build
WORKDIR /src
COPY Api.csproj .
RUN dotnet restore
COPY Program.cs .
RUN dotnet publish -c Release --no-restore -o /app /p:UseAppHost=false
FROM mcr.microsoft.com/dotnet/aspnet:11.0.0-rc.2-resolute
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_URLS=http://+:8080 DOTNET_NOLOGO=true
ENTRYPOINT ["dotnet", "Api.dll"]
