import type { Role } from "../api/types";

const operatorRoles = new Set<Role>(["Admin", "Operator", "SupportEngineer"]);
const adminRoles = new Set<Role>(["Admin", "SupportEngineer"]);

export function canAcknowledge(role: Role | undefined) {
  return role !== undefined && operatorRoles.has(role);
}

export function canRunReports(role: Role | undefined) {
  return role !== undefined && operatorRoles.has(role);
}

export function canViewAdmin(role: Role | undefined) {
  return role !== undefined && adminRoles.has(role);
}
