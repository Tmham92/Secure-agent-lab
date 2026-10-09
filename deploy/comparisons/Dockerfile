FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /source
COPY . .
RUN dotnet publish src/SecureAgentLab.Comparisons -c Release -o /out
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /out .
RUN touch /outer-contained /SecureAgentLab.slnx && mkdir /fake
USER 1654:1654
ENTRYPOINT ["dotnet", "SecureAgentLab.Comparisons.dll", "--container"]
