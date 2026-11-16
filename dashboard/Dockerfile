FROM node:22-alpine AS build
WORKDIR /app
COPY package.json package-lock.json ./
RUN npm ci
COPY . .
# Sign-in settings are compiled into the bundle. Left empty, the dashboard runs without sign-in (local use only).
ARG VITE_OIDC_AUTHORITY=""
ARG VITE_OIDC_CLIENT_ID=""
ARG VITE_OIDC_SCOPE=""
RUN npm run build

FROM nginx:alpine
COPY --from=build /app/dist /usr/share/nginx/html
COPY nginx.conf.template /etc/nginx/templates/default.conf.template
ENV API_UPSTREAM=http://overwatch-api:3001
# Origin of the sign-in server, added to the CSP so the browser may call its discovery and token endpoints.
# Empty by default; it must still be defined, because the template step only substitutes variables that exist.
ENV OIDC_ORIGIN=""
EXPOSE 3000
