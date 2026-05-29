# RATools Frontend

React-based UI for the RATools eCTD publishing system.

## Development

Install dependencies and start the Vite dev server:

```bash
npm install
npm run dev
```

The dev server runs on `http://localhost:3000` and proxies `/api` and `/health` requests to the backend at `http://localhost:5000`.

## Testing

Run tests once:

```bash
npm test
```

## Build

Create a production build:

```bash
npm run build
```

Build output is written to `dist/`.

## Configuration

- Backend proxy configuration lives in `vite.config.ts`.
- The default development backend is `http://localhost:5000`.
- Production API requests use same-origin `/api` paths.
