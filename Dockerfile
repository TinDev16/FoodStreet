# Build
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /app

COPY . ./

# Publish API
RUN dotnet publish FoodStreetPoiAdmin/FoodStreetPoiAdmin.csproj -c Release -o out

# Runtime
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app

# Copy code đã build
COPY --from=build /app/out .

COPY FoodStreetPoiAdmin/App_Data/poi-admin.db3 ./App_Data/poi-admin.db3

CMD ["dotnet", "FoodStreetPoiAdmin.dll"]