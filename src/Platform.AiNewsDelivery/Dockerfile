FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copia os arquivos de projeto primeiro para cache da camada de restore
COPY ["src/Platform.AiNewsDelivery/Platform.AiNewsDelivery.csproj", "src/Platform.AiNewsDelivery/"]
COPY ["src/Platform.Contracts/Platform.Contracts.csproj", "src/Platform.Contracts/"]
RUN dotnet restore "src/Platform.AiNewsDelivery/Platform.AiNewsDelivery.csproj"

# Copia todo o resto do código
COPY . .
WORKDIR "/src/src/Platform.AiNewsDelivery"

# Compila o projeto
RUN dotnet build "Platform.AiNewsDelivery.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "Platform.AiNewsDelivery.csproj" -c Release -o /app/publish /p:UseAppHost=false

# Imagem final baseada em Azure Functions
FROM mcr.microsoft.com/azure-functions/dotnet-isolated:4-dotnet-isolated8.0
ENV AzureWebJobsScriptRoot=/home/site/wwwroot \
    AzureFunctionsJobHost__Logging__Console__IsEnabled=true

COPY --from=publish /app/publish /home/site/wwwroot
