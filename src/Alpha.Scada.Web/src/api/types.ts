export type ScreenName = "Overview" | "Tags" | "Trends" | "Alarms" | "Reports" | "Admin";

export type User = {
  userId: string;
  tenantId: string;
  email: string;
  displayName: string;
  role: string;
};

export type LoginResponse = {
  accessToken: string;
  expiresAtUtc: string;
  user: {
    id: string;
    tenantId: string;
    email: string;
    displayName: string;
    role: string;
  };
};

export type Tenant = { id: string; key: string; name: string; region: string };
export type Site = { id: string; tenantId: string; key: string; name: string; region: string; status: string };
export type Unit = { id: string; tenantId: string; siteId: string; key: string; name: string; model: string; status: string; lastSeenUtc?: string };
export type Tag = { tagId: string; tenantId: string; unitId: string; tagKey: string; name: string; subsystem: string; value: number; engineeringUnit: string; quality: string; timestampUtc: string };
export type TelemetryUpdateSample = { tagId: string; tagKey: string; value: number; quality: string; timestampUtc: string };
export type TelemetryUpdate = { tenantId: string; unitId: string; storedAtUtc: string; samples: TelemetryUpdateSample[] };
export type HistoryPoint = { timestampUtc: string; value: number; quality: string };
export type Alarm = { id: string; unitId: string; tagId?: string; severity: string; message: string; state: string; raisedAtUtc: string; acknowledgedAtUtc?: string; clearedAtUtc?: string };
export type Report = { id: string; unitId: string; period: string; electricalKwh: number; thermalKwh: number; runtimeHours: number; availabilityPercent: number; estimatedWoodChipsKg: number; estimatedBiocharM3: number; alarmCount: number; generatedAtUtc: string };
export type SystemProbe = { health: string; ready: string; metrics: string };
