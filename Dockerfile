FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY VikunjaDiscordGateway.csproj ./
RUN dotnet restore VikunjaDiscordGateway.csproj
COPY . ./
RUN dotnet publish VikunjaDiscordGateway.csproj -c Release -o /app/publish --no-restore /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build /app/publish ./
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
USER $APP_UID
ENTRYPOINT ["dotnet", "VikunjaDiscordGateway.dll"]
