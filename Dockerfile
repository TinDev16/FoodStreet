FROM mcr.microsoft.com/dotnet/sdk:10.0 AS restore
WORKDIR /src

COPY FoodStreetPoiAdmin/FoodStreetPoiAdmin.csproj FoodStreetPoiAdmin/
RUN dotnet restore FoodStreetPoiAdmin/FoodStreetPoiAdmin.csproj

FROM restore AS publish
COPY . .
RUN dotnet publish FoodStreetPoiAdmin/FoodStreetPoiAdmin.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

COPY --from=publish /app/publish .
RUN mkdir -p /app/App_Data

ENV ASPNETCORE_URLS=http://0.0.0.0:5187
ENV DOTNET_EnableDiagnostics=0
EXPOSE 5187

ENTRYPOINT ["dotnet", "FoodStreetPoiAdmin.dll"]