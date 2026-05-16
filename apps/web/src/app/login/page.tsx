import { Activity, LockKeyhole } from "lucide-react";
import { loginAction } from "./actions";
import { sanitizeNextPath } from "@/lib/auth-routing";

type LoginPageProps = {
  searchParams: Promise<{
    error?: string;
    next?: string;
  }>;
};

export const dynamic = "force-dynamic";

export default async function LoginPage({ searchParams }: LoginPageProps) {
  const params = await searchParams;
  const nextPath = sanitizeNextPath(params.next);
  const hasError = params.error === "1";

  return (
    <main className="loginShell">
      <section className="loginPanel" aria-labelledby="login-title">
        <div className="loginBrand">
          <div className="brandMark">
            <Activity aria-hidden="true" />
          </div>
          <div>
            <span className="brandName">OpsPulse</span>
            <span className="brandMeta">Secure dashboard access</span>
          </div>
        </div>

        <div className="loginHeader">
          <div className="loginIcon">
            <LockKeyhole aria-hidden="true" />
          </div>
          <div>
            <h1 id="login-title">Enter dashboard password</h1>
            <p>Protected monitoring and SRE controls.</p>
          </div>
        </div>

        <form action={loginAction} className="loginForm">
          <input name="next" type="hidden" value={nextPath} />
          <label htmlFor="password">Password</label>
          <input
            autoComplete="current-password"
            autoFocus
            id="password"
            name="password"
            required
            type="password"
          />
          {hasError && (
            <p className="loginError" role="alert">
              Invalid password.
            </p>
          )}
          <button type="submit">Unlock</button>
        </form>
      </section>
    </main>
  );
}
