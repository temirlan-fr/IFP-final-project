FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /source
COPY global.json ./
COPY src/QueueLess.Core/QueueLess.Core.csproj src/QueueLess.Core/
COPY src/QueueLess.Infrastructure/QueueLess.Infrastructure.csproj src/QueueLess.Infrastructure/
COPY src/QueueLess.Web/QueueLess.Web.csproj src/QueueLess.Web/
RUN dotnet restore src/QueueLess.Web/QueueLess.Web.csproj
COPY src/ src/
RUN dotnet publish src/QueueLess.Web/QueueLess.Web.csproj --configuration Release --no-restore --output /publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /publish ./
RUN mkdir -p /data && chown app:app /data
ENV ASPNETCORE_ENVIRONMENT=Production \
    ASPNETCORE_HTTP_PORTS=8080 \
    Storage__DataDirectory=/data
EXPOSE 8080
USER app
ENTRYPOINT ["dotnet", "QueueLess.Web.dll"]
