# ===== Giai đoạn build: biên dịch bên trong container Linux (tránh Smart App Control) =====
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy file project trước để tận dụng cache khi restore.
COPY Event.Api.csproj ./
RUN dotnet restore Event.Api.csproj

# Copy phần còn lại và publish ra thư mục /app.
COPY . ./
RUN dotnet publish Event.Api.csproj -c Release -o /app

# ===== Giai đoạn runtime: image gọn chỉ chứa ASP.NET runtime =====
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app ./

# Lắng nghe cổng 8080 bên trong container.
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "Event.Api.dll"]
