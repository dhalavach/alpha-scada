import * as signalR from "@microsoft/signalr";
import { useEffect } from "react";
import { apiBase } from "../api/client";

type UseSignalRArgs = {
  token: string;
  selectedUnitId: string;
  setStatus: (status: string) => void;
  loadUnit: (unitId: string) => Promise<void>;
  loadAlarms: () => Promise<void>;
  loadSitesAndUnits: () => Promise<void>;
  onReportCompleted: () => Promise<void>;
};

export default function useSignalR({
  token,
  selectedUnitId,
  setStatus,
  loadUnit,
  loadAlarms,
  loadSitesAndUnits,
  onReportCompleted
}: UseSignalRArgs) {
  useEffect(() => {
    if (!token) return;

    const connection = new signalR.HubConnectionBuilder()
      .withUrl(`${apiBase}/hubs/telemetry`, { accessTokenFactory: () => token })
      .withAutomaticReconnect()
      .build();

    connection.onreconnecting(() => setStatus("Reconnecting"));
    connection.onreconnected(() => setStatus("Live"));
    connection.onclose(() => setStatus("Disconnected"));
    connection.on("telemetryUpdated", () => {
      if (selectedUnitId) loadUnit(selectedUnitId);
    });
    connection.on("alarmsChanged", loadAlarms);
    connection.on("unitStatusChanged", loadSitesAndUnits);
    connection.on("reportCompleted", onReportCompleted);

    connection.start()
      .then(() => setStatus("Live"))
      .catch(() => setStatus("Offline"));

    return () => {
      connection.stop();
    };
  }, [token, selectedUnitId]);
}
