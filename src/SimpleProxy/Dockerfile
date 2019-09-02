FROM mcr.microsoft.com/dotnet/core/aspnet:2.2-stretch-slim AS base
WORKDIR /app
EXPOSE 80

FROM mcr.microsoft.com/dotnet/core/sdk:2.2-stretch AS build
WORKDIR /src
COPY ["src/SimpleProxy/SimpleProxy.csproj", "src/SimpleProxy/"]
RUN dotnet restore "src/SimpleProxy/SimpleProxy.csproj"
COPY . .
WORKDIR "/src/src/SimpleProxy"
RUN dotnet build "SimpleProxy.csproj" -c Release -o /app

FROM build AS publish
RUN dotnet publish "SimpleProxy.csproj" -c Release -o /app

FROM base AS final
WORKDIR /app
COPY --from=publish /app .
ENTRYPOINT ["dotnet", "SimpleProxy.dll"]