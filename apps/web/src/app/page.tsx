import { DashboardShell } from "@/components/dashboard/DashboardShell";
import { getSrePayload } from "@/lib/api";

export const dynamic = "force-dynamic";

export default async function Home() {
  const payload = await getSrePayload();

  return <DashboardShell payload={payload} />;
}
