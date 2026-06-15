import { Component, type ErrorInfo, type ReactNode } from "react";

type ErrorBoundaryProps = {
  children: ReactNode;
};

type ErrorBoundaryState = {
  error: Error | null;
};

export default class ErrorBoundary extends Component<ErrorBoundaryProps, ErrorBoundaryState> {
  state: ErrorBoundaryState = { error: null };

  static getDerivedStateFromError(error: Error): ErrorBoundaryState {
    return { error };
  }

  componentDidCatch(error: Error, info: ErrorInfo) {
    console.error("Alpha SCADA workspace failed to render", error, info);
  }

  render() {
    if (this.state.error) {
      return (
        <main className="login">
          <section className="panel fatalError">
            <p className="eyebrow">Workspace error</p>
            <h1>Unable to render the application</h1>
            <p className="error">{this.state.error.message}</p>
            <button className="primaryButton" onClick={() => window.location.reload()}>Reload</button>
          </section>
        </main>
      );
    }

    return this.props.children;
  }
}
