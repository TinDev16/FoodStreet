FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /app

COPY . ./
COPY FoodStreetPoiAdmin/App_Data/poi-admin.db3 ./App_Data/poi-admin.db3

RUN dotnet publish FoodStreetPoiAdmin/FoodStreetPoiAdmin.csproj -c Release -o out

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app/out .

CMD ["dotnet", "FoodStreetPoiAdmin.dll"]