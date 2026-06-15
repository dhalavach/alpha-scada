import react from "@vitejs/plugin-react";
import { defineConfig } from "vite";

export default defineConfig({
  plugins: [react()],
  server: {
    port: 5173,
    proxy: {
      "/api": "http://localhost:5202",
      "/health": "http://localhost:5202",
      "/ready": "http://localhost:5202",
      "/hubs": {
        target: "http://localhost:5202",
        ws: true
      }
    }
  }
});
