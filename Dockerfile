# Build from the solution root so the API can access ClaimWise.Rag.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY src/ClaimWise.Rag/ClaimWise.Rag.csproj src/ClaimWise.Rag/
COPY src/ClaimWise.Api/ClaimWise.Api.csproj src/ClaimWise.Api/

RUN dotnet restore src/ClaimWise.Api/ClaimWise.Api.csproj

COPY . .
RUN dotnet publish src/ClaimWise.Api/ClaimWise.Api.csproj \
    --configuration Release \
    --no-restore \
    --output /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

COPY --from=build /app/publish .

ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "ClaimWise.Api.dll"]