# Build context: repository root.
#   podman build -f src/SkailPaymentWebhook.Worker/Dockerfile -t payment-webhook-worker .
# dotnet publish runs in the SDK image; the final image only needs the .NET runtime.
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY nuget.config ./
COPY src/SkailPaymentWebhook.Contracts/SkailPaymentWebhook.Contracts.csproj src/SkailPaymentWebhook.Contracts/
COPY src/SkailPaymentWebhook.Worker/SkailPaymentWebhook.Worker.csproj src/SkailPaymentWebhook.Worker/
RUN dotnet restore src/SkailPaymentWebhook.Worker/SkailPaymentWebhook.Worker.csproj
COPY src/SkailPaymentWebhook.Contracts src/SkailPaymentWebhook.Contracts
COPY src/SkailPaymentWebhook.Worker src/SkailPaymentWebhook.Worker
RUN dotnet publish src/SkailPaymentWebhook.Worker/SkailPaymentWebhook.Worker.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/runtime:8.0
WORKDIR /app
COPY --from=build /app .
ENTRYPOINT ["dotnet", "SkailPaymentWebhook.Worker.dll"]
