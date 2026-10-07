FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY ["AIOrchestrator.ChatServer/AIOrchestrator.ChatServer.csproj", "AIOrchestrator.ChatServer/"]
RUN dotnet restore "AIOrchestrator.ChatServer/AIOrchestrator.ChatServer.csproj"
COPY . .
RUN dotnet publish "AIOrchestrator.ChatServer/AIOrchestrator.ChatServer.csproj" \
    --configuration Release \
    --no-restore \
    --output /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app/publish .
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "AIOrchestrator.ChatServer.dll"]
