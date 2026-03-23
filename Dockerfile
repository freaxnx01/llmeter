FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY LLMeter.slnx .
COPY src/LLMeter/LLMeter.csproj src/LLMeter/
COPY tests/LLMeter.Tests/LLMeter.Tests.csproj tests/LLMeter.Tests/
RUN dotnet restore
COPY . .
RUN dotnet publish src/LLMeter/LLMeter.csproj -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080
ENTRYPOINT ["dotnet", "LLMeter.dll"]
