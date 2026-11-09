FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY Directory.Build.props .
COPY DeveloperOverwatch.slnx .
COPY src/Domain/DeveloperOverwatch.Domain/DeveloperOverwatch.Domain.csproj src/Domain/DeveloperOverwatch.Domain/
COPY src/Application/DeveloperOverwatch.Application/DeveloperOverwatch.Application.csproj src/Application/DeveloperOverwatch.Application/
COPY src/Infrastructure/DeveloperOverwatch.Infrastructure/DeveloperOverwatch.Infrastructure.csproj src/Infrastructure/DeveloperOverwatch.Infrastructure/
COPY src/API/DeveloperOverwatch.API/DeveloperOverwatch.API.csproj src/API/DeveloperOverwatch.API/

RUN dotnet restore src/API/DeveloperOverwatch.API/DeveloperOverwatch.API.csproj

COPY src/ src/
RUN dotnet publish src/API/DeveloperOverwatch.API/DeveloperOverwatch.API.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
RUN mkdir -p /data && chown -R app:app /data

ENV ASPNETCORE_URLS=http://+:3001
ENV Database__ConnectionString="Data Source=/data/overwatch.db"

EXPOSE 3001
USER app
ENTRYPOINT ["dotnet", "DeveloperOverwatch.API.dll"]
