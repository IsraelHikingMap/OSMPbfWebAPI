FROM mcr.microsoft.com/dotnet/sdk:5.0 AS build-env
WORKDIR /app
COPY . ./
RUN dotnet publish -c Release -o out

# Build runtime image
FROM mcr.microsoft.com/dotnet/aspnet:5.0
WORKDIR /app
RUN apt-get update -y && apt-get install -y osmctools
COPY --from=build-env /app/out .
EXPOSE 80
ENTRYPOINT ["dotnet", "OSMPbfWebAPI.dll"]
