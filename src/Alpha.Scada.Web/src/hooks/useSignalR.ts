import * as signalR from "@microsoft/signalr";
import { useEffect, useRef } from "react";
import { apiBase } from "../api/client";
import type { TelemetryUpdate } from "../api/types";

type UseSignalRArgs = {
  token: string;
  setStatus: (status: string) => void;
  applyTelemetryUpdate: (update: TelemetryUpdate) => void;
  loadAlarms: () => Promise<void>;
  loadSitesAndUnits: () => Promise<void>;
  onReportCompleted: () => Promise<void>;
};

export default function useSignalR({
  token,
  setStatus,
  applyTelemetryUpdate,
  loadAlarms,
  loadSitesAndUnits,
  onReportCompleted
}: UseSignalRArgs) {
  const handlers = useRef({
    applyTelemetryUpdate,
    loadAlarms,
    loadSitesAndUnits,
    onReportCompleted
  });

  useEffect(() => {
    handlers.current = {
      applyTelemetryUpdate,
      loadAlarms,
      loadSitesAndUnits,
      onReportCompleted
    };
  });

  useEffect(() => {
    if (!token) return;
    let unitRefreshTimer: ReturnType<typeof window.setTimeout> | undefined;

    const connection = new signalR.HubConnectionBuilder()
      .withUrl(`${apiBase}/hubs/telemetry`, { accessTokenFactory: () => token })
      .withAutomaticReconnect()
      .build();

    connection.onreconnecting(() => setStatus("Reconnecting"));
    connection.onreconnected(() => setStatus("Live"));
    connection.onclose(() => setStatus("Disconnected"));
    connection.on("telemetryUpdated", (update: TelemetryUpdate) => {
      handlers.current.applyTelemetryUpdate(update);
    });
    connection.on("alarmsChanged", () => {
      void handlers.current.loadAlarms();
    });
    connection.on("unitStatusChanged", () => {
      if (unitRefreshTimer) {
        window.clearTimeout(unitRefreshTimer);
      }

      unitRefreshTimer = window.setTimeout(() => {
        unitRefreshTimer = undefined;
        void handlers.current.loadSitesAndUnits();
      }, 1000);
    });
    connection.on("reportCompleted", () => {
      void handlers.current.onReportCompleted();
    });

    connection.start()
      .then(() => setStatus("Live"))
      .catch(() => setStatus("Offline"));

    return () => {
      if (unitRefreshTimer) {
        window.clearTimeout(unitRefreshTimer);
      }

      connection.stop();
    };
  }, [token, setStatus]);
}
