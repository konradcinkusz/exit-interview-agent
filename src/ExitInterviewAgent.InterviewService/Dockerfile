# P6: multi-stage, repository root as build context (`docker build -f <this file> .`).
# Runtime image major == TFM major (net10.0): roll-forward does not cross a major.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Project files first, so a source change does not invalidate the restore layer.
COPY global.json Directory.Build.props Directory.Packages.props ./
COPY src/ExitInterviewAgent.ServiceDefaults/ExitInterviewAgent.ServiceDefaults.csproj src/ExitInterviewAgent.ServiceDefaults/
COPY src/ExitInterviewAgent.Contracts/ExitInterviewAgent.Contracts.csproj src/ExitInterviewAgent.Contracts/
COPY src/ExitInterviewAgent.InterviewService/ExitInterviewAgent.InterviewService.csproj src/ExitInterviewAgent.InterviewService/
RUN dotnet restore src/ExitInterviewAgent.InterviewService/ExitInterviewAgent.InterviewService.csproj

COPY src/ExitInterviewAgent.ServiceDefaults/ src/ExitInterviewAgent.ServiceDefaults/
COPY src/ExitInterviewAgent.Contracts/ src/ExitInterviewAgent.Contracts/
COPY src/ExitInterviewAgent.InterviewService/ src/ExitInterviewAgent.InterviewService/
RUN dotnet publish src/ExitInterviewAgent.InterviewService/ExitInterviewAgent.InterviewService.csproj \
    -c Release -o /app/publish --no-restore /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
LABEL org.opencontainers.image.source="https://github.com/konradcinkusz/exit-interview-agent"
LABEL org.opencontainers.image.description="exit-interview-agent interview-service"
LABEL org.opencontainers.image.licenses="MIT"
WORKDIR /app
COPY --from=build /app/publish .
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
# Non-root: the aspnet base image ships an unprivileged `app` user.
USER app
ENTRYPOINT ["dotnet", "ExitInterviewAgent.InterviewService.dll"]
