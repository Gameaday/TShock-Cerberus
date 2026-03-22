FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
COPY ["CerberusBot.csproj", "./"]
RUN dotnet restore "CerberusBot.csproj"
COPY . .
RUN dotnet publish "CerberusBot.csproj" -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS final
WORKDIR /app
COPY --from=build /app/publish .
# Ensure the shared directory exists in the container
RUN mkdir -p /app/shared 
ENTRYPOINT ["dotnet", "CerberusBot.dll"]