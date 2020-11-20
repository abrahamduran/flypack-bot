#See https://aka.ms/containerfastmode to understand how Visual Studio uses this Dockerfile to build your images for faster debugging.

FROM mcr.microsoft.com/dotnet/core/aspnet:3.1-buster-slim-arm64v8 AS base
WORKDIR /app

FROM mcr.microsoft.com/dotnet/core/sdk:3.1-buster AS build
WORKDIR /src
COPY FlypackBot/FlypackBot.csproj FlypackBot/
RUN dotnet restore "FlypackBot/FlypackBot.csproj"
COPY . .
WORKDIR "/src/FlypackBot"
RUN dotnet build "FlypackBot.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "FlypackBot.csproj" -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "FlypackBot.dll"]
