# AION - Build stage
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# Backend
COPY Aion.Core/ Aion.Core/
COPY Aion.Host/ Aion.Host/
RUN dotnet restore Aion.Host/Aion.Host.csproj
RUN dotnet publish Aion.Host/Aion.Host.csproj -c Release -o /app/backend

# Frontend
FROM node:20-alpine AS frontend-build
WORKDIR /ui
COPY aion-ui/ .
RUN npm ci && npm run build

# Runtime
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
WORKDIR /app

ENV ASPNETCORE_URLS=http://+:6969
ENV AION_CONFIG_DIR=/home/aion/.aion
ENV AION_WORKSPACE=/workspace

RUN addgroup --system --gid 1001 aion && \
    adduser --system --uid 1001 --ingroup aion aion && \
    mkdir -p /workspace && \
    chown aion:aion /workspace

COPY --from=build /app/backend .
COPY --from=frontend-build /ui/dist ./wwwroot

RUN chown -R aion:aion /app

USER aion
EXPOSE 6969 6970 6971

VOLUME [ "/home/aion/.aion", "/workspace" ]
HEALTHCHECK --interval=30s --timeout=5s --retries=3 \
    CMD curl -f http://localhost:6969/api/health || exit 1

ENTRYPOINT [ "dotnet", "Aion.Host.dll" ]
