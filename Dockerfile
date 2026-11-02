FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY Shared ./Shared
COPY loja_s ./loja_s
RUN dotnet publish loja_s/loja_s.csproj -c Release -o /out

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /out .
COPY loja_s/loja_s.db ./loja_s.db
ENV ASPNETCORE_FORWARDEDHEADERS_ENABLED=true
CMD ["sh", "-c", "ASPNETCORE_URLS=http://0.0.0.0:${PORT:-8080} dotnet loja_s.dll"]