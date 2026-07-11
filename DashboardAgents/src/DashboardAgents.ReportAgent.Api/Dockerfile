# syntax=docker/dockerfile:1

# ---- Stage 1: build the .NET API -------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY DashboardAgents.ReportAgent.Api.csproj ./
RUN dotnet restore ./DashboardAgents.ReportAgent.Api.csproj
COPY . .
RUN dotnet publish ./DashboardAgents.ReportAgent.Api.csproj -c Release -o /out

# ---- Stage 2: runtime -------------------------------------------------------
FROM mcr.microsoft.com/dotnet/aspnet:8.0

# Python runtime for the report engine, kept minimal — no compilers/dev tools
# are left in the final image (venv install below happens in this same layer
# then the apt cache is dropped).
RUN apt-get update && apt-get install -y --no-install-recommends \
        python3 python3-pip python3-venv \
    && rm -rf /var/lib/apt/lists/*

# Non-root user for everything — the .NET API and the Python subprocess it
# spawns both run as this account, never root.
RUN useradd --create-home --shell /usr/sbin/nologin reportagent

WORKDIR /app
COPY --from=build /out ./
COPY python_agent /app/python_agent

RUN python3 -m venv /opt/venv \
    && /opt/venv/bin/pip install --no-cache-dir -r /app/python_agent/requirements.txt

# Dedicated scratch mount for per-request temp files — mount this as tmpfs
# at deploy time so it never touches persistent disk and is wiped on restart.
RUN mkdir -p /scratch && chown reportagent:reportagent /scratch
VOLUME ["/scratch"]

RUN chown -R reportagent:reportagent /app

USER reportagent
EXPOSE 5080
ENV ASPNETCORE_URLS=http://0.0.0.0:5080
ENTRYPOINT ["dotnet", "DashboardAgents.ReportAgent.Api.dll"]
