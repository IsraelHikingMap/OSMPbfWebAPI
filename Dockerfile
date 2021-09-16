FROM mcr.microsoft.com/dotnet/sdk:5.0-focal AS build-env
WORKDIR /app
COPY . ./
RUN dotnet publish -c Release -o out

# Build runtime image
FROM mcr.microsoft.com/dotnet/aspnet:5.0
WORKDIR /app
RUN apt-get update -y && apt-get install -y osmctools && apt-get install -y pyosmium && apt-get install -y curl
COPY --from=build-env /app/out .
EXPOSE 80
HEALTHCHECK --interval=5s --timeout=3s CMD curl --fail http://localhost:80/api/health || exit 1
VOLUME ["/app/containers"]
ENTRYPOINT ["dotnet", "OSMPbfWebAPI.dll"]
