import { useState } from "react";

type LoginProps = {
  onLogin: (email: string, password: string) => Promise<void>;
};

export default function Login({ onLogin }: LoginProps) {
  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");
  const [error, setError] = useState("");

  return (
    <main className="login">
      <form onSubmit={async event => {
        event.preventDefault();
        setError("");
        try {
          await onLogin(email, password);
        } catch {
          setError("Login failed");
        }
      }}>
        <span className="brandMark large">A</span>
        <p className="eyebrow">Alpha SCADA</p>
        <h1>Sign in</h1>
        <label>Email<input value={email} autoComplete="username" required onChange={event => setEmail(event.target.value)} /></label>
        <label>Password<input type="password" value={password} autoComplete="current-password" required onChange={event => setPassword(event.target.value)} /></label>
        {error && <p className="error">{error}</p>}
        <button className="primaryButton" type="submit">Login</button>
      </form>
    </main>
  );
}
