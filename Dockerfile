FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS base
USER $APP_UID
WORKDIR /app
EXPOSE 7198
EXPOSE 5192

FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["RetrievalApiMcpServer.csproj", "./"]
RUN dotnet restore "RetrievalApiMcpServer.csproj"
COPY . .
WORKDIR "/src/"
RUN dotnet build "./RetrievalApiMcpServer.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./RetrievalApiMcpServer.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

COPY ./mcp.json /etc/mcp/server.json

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "RetrievalApiMcpServer.dll"]

LABEL org.opencontainers.image.title="copilot-retrieval-mcp"
LABEL org.opencontainers.image.description="An MCP server for Microsoft 365 Copilot Retrieval API."
LABEL org.opencontainers.image.source="https://github.com/lowcodelegend/copilot-retrieval-api-mcp-server"
LABEL org.mcp.server.version="1.0.0"
LABEL org.mcp.server.type="http"
LABEL org.mcp.server.transport="http"
LABEL org.mcp.server.port="5192"
LABEL org.mcp.server.tools="copilotRetrievalSearch"
