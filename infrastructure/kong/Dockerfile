FROM kong:3.9
COPY --chown=kong:kong infrastructure/kong/kong.yml /usr/local/kong/declarative/kong.yml
ENV KONG_DATABASE=off
ENV KONG_DECLARATIVE_CONFIG=/usr/local/kong/declarative/kong.yml
USER kong
