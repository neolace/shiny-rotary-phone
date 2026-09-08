FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY src/EntraAuth.AspNetCore/EntraAuth.AspNetCore.csproj src/EntraAuth.AspNetCore/
COPY samples/SampleApi/SampleApi.csproj samples/SampleApi/
RUN dotnet restore samples/SampleApi/SampleApi.csproj
COPY src/EntraAuth.AspNetCore/ src/EntraAuth.AspNetCore/
COPY samples/SampleApi/ samples/SampleApi/
RUN dotnet publish samples/SampleApi/SampleApi.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "SampleApi.dll"]
