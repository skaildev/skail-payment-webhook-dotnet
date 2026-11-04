# Build context: repository root.
#   podman build -f src/SkailPaymentWebhook.Web/Dockerfile -t payment-webhook-web .
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY nuget.config ./
COPY src/SkailPaymentWebhook.Contracts/SkailPaymentWebhook.Contracts.csproj src/SkailPaymentWebhook.Contracts/
COPY src/SkailPaymentWebhook.Web/SkailPaymentWebhook.Web.csproj src/SkailPaymentWebhook.Web/
RUN dotnet restore src/SkailPaymentWebhook.Web/SkailPaymentWebhook.Web.csproj
COPY src/SkailPaymentWebhook.Contracts src/SkailPaymentWebhook.Contracts
COPY src/SkailPaymentWebhook.Web src/SkailPaymentWebhook.Web
RUN dotnet publish src/SkailPaymentWebhook.Web/SkailPaymentWebhook.Web.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "SkailPaymentWebhook.Web.dll"]
