import type { Tag, Unit } from "../api/types";

export const processSteps = [
  { label: "Fuel Feed", key: "fuel.wood_chip_feed_kg_h", unit: "kg/h" },
  { label: "Gasifier", key: "gasifier.reactor_temp_c", unit: "degC" },
  { label: "Gas Cleaning", key: "gas_cleaning.filter_dp_mbar", unit: "mbar" },
  { label: "Engine", key: "engine.electrical_output_kw", unit: "kW" },
  { label: "Heat Recovery", key: "heat.thermal_output_kw", unit: "kW" }
];

export function tagValue(tags: Tag[], key: string, unit: string) {
  const tag = tags.find(item => item.tagKey === key);
  return tag ? `${format(tag.value)} ${unit}` : "--";
}

export function unitName(units: Unit[], unitId: string) {
  return units.find(unit => unit.id === unitId)?.name ?? "Unit";
}

export function format(value: number) {
  return Number(value).toLocaleString(undefined, { maximumFractionDigits: 1 });
}

export function groupBy<T>(items: T[], selector: (item: T) => string) {
  return items.reduce<Record<string, T[]>>((groups, item) => {
    const key = selector(item);
    groups[key] ??= [];
    groups[key].push(item);
    return groups;
  }, {});
}
