import { Component, type ReactNode } from 'react';

interface Props {
  /** What failed, for the message ("the Files view"). */
  what: string;
  children: ReactNode;
}

/**
 * Keeps a failure in one view from blanking the whole window. State kept outside React (the editor's open files and
 * their unsaved edits) survives, and "Try again" draws the view afresh.
 */
export class ErrorBoundary extends Component<Props, { error: Error | null }> {
  state = { error: null as Error | null };

  static getDerivedStateFromError(error: Error) {
    return { error };
  }

  render() {
    if (!this.state.error) return this.props.children;
    return (
      <div className="files-empty" role="alert">
        <p className="danger-text">
          Something went wrong in {this.props.what}: {this.state.error.message}
        </p>
        <button type="button" onClick={() => this.setState({ error: null })}>
          Try again
        </button>
      </div>
    );
  }
}
