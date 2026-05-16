import { Activity, Shield, Zap, Server } from "lucide-react";
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
      {/* Animated background orbs */}
      <div className="loginBgOrb loginBgOrb--1" aria-hidden="true" />
      <div className="loginBgOrb loginBgOrb--2" aria-hidden="true" />
      <div className="loginBgOrb loginBgOrb--3" aria-hidden="true" />

      {/* Grid noise texture */}
      <div className="loginGrid" aria-hidden="true" />

      <div className="loginLayout">
        {/* Left panel — branding */}
        <div className="loginLeft" aria-hidden="true">
          <div className="loginLeftInner">
            <div className="loginLogoWrap">
              <Activity className="loginLogoIcon" />
            </div>
            <h2 className="loginLeftTitle">OpsPulse</h2>
            <p className="loginLeftSub">
              Real-time infrastructure monitoring.<br />
              Stay ahead of every incident.
            </p>

            <div className="loginFeatures">
              <div className="loginFeatureItem">
                <span className="loginFeatureDot loginFeatureDot--green" />
                <span>Live system metrics & SLO tracking</span>
              </div>
              <div className="loginFeatureItem">
                <span className="loginFeatureDot loginFeatureDot--blue" />
                <span>Multi-service health at a glance</span>
              </div>
              <div className="loginFeatureItem">
                <span className="loginFeatureDot loginFeatureDot--purple" />
                <span>Alerting & incident management</span>
              </div>
            </div>

            <div className="loginStatRow">
              <div className="loginStat">
                <Zap className="loginStatIcon" />
                <span className="loginStatVal">99.9%</span>
                <span className="loginStatLabel">Uptime SLO</span>
              </div>
              <div className="loginStatDivider" />
              <div className="loginStat">
                <Server className="loginStatIcon" />
                <span className="loginStatVal">24/7</span>
                <span className="loginStatLabel">Monitoring</span>
              </div>
              <div className="loginStatDivider" />
              <div className="loginStat">
                <Shield className="loginStatIcon" />
                <span className="loginStatVal">E2E</span>
                <span className="loginStatLabel">Encrypted</span>
              </div>
            </div>
          </div>
        </div>

        {/* Right panel — form */}
        <div className="loginRight">
          <section className="loginCard" aria-labelledby="login-title">
            {/* Header */}
            <div className="loginCardHeader">
              <div className="loginAvatarRing">
                <div className="loginAvatar">
                  <span>R</span>
                </div>
              </div>
              <p className="loginWelcomeSub">Good to see you again 👋</p>
              <h1 id="login-title" className="loginWelcomeTitle">
                Welcome back,<br />
                <span className="loginWelcomeName">Ratchanon</span>
              </h1>
              <p className="loginWelcomeDesc">
                Enter your dashboard password to continue.
              </p>
            </div>

            {/* Form */}
            <form action={loginAction} className="loginForm">
              <input name="next" type="hidden" value={nextPath} />

              <div className="loginFieldWrap">
                <label htmlFor="password" className="loginLabel">
                  Password
                </label>
                <div className="loginInputWrap">
                  <Shield className="loginInputIcon" aria-hidden="true" />
                  <input
                    autoComplete="current-password"
                    autoFocus
                    id="password"
                    name="password"
                    required
                    type="password"
                    className="loginInput"
                    placeholder="Enter your password"
                  />
                </div>
                {hasError && (
                  <p className="loginError" role="alert">
                    Incorrect password. Please try again.
                  </p>
                )}
              </div>

              <button type="submit" className="loginBtn" id="login-submit-btn">
                <span>Unlock Dashboard</span>
                <span className="loginBtnArrow">→</span>
              </button>
            </form>

            <p className="loginFootnote">
              <Shield aria-hidden="true" className="loginFootnoteIcon" />
              Secured with HMAC-SHA256 session tokens
            </p>
          </section>
        </div>
      </div>
    </main>
  );
}
