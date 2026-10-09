FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /source
COPY . .
RUN dotnet publish src/SecureAgentLab.Worker -c Release -o /out/worker && \
    dotnet publish src/SecureAgentLab.Api -c Release -o /out/gateway && \
    dotnet publish src/SecureAgentLab.Operator -c Release -o /out/operator

FROM mcr.microsoft.com/dotnet/runtime:10.0 AS worker
WORKDIR /app
COPY --from=build /out/worker .
USER 1654:1654
ENTRYPOINT ["dotnet", "SecureAgentLab.Worker.dll"]

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS gateway
WORKDIR /app
COPY --from=build /out/gateway .
COPY --from=build /out/operator /operator
USER 1656:1656
ENTRYPOINT ["dotnet", "SecureAgentLab.Api.dll"]
