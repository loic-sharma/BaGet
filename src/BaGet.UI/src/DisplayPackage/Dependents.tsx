import { config } from '../config';
import * as React from 'react';

interface IDependentsProps {
  packageId: string;
}

interface IDependentsState {
  totalHits: number | undefined;
  data: string[] | undefined;
}

class Dependents extends React.Component<IDependentsProps, IDependentsState> {

  private controller: AbortController;

  constructor(props: IDependentsProps) {
    super(props);

    this.controller = new AbortController();

    this.state = {
      data: undefined,
      totalHits: undefined,
    };
  }

  public componentWillUnmount() {
    this.controller.abort();
  }

  public componentDidMount() {
    const url = `${config.apiUrl}/v3/dependents?packageId=${this.props.packageId}`;

    fetch(url, {signal: this.controller.signal}).then(response => {
      return response.json();
    }).then(json => {
      // tslint:disable-next-line:no-console
      console.log(json as IDependentsState);
      this.setState(json as IDependentsState);
    // tslint:disable-next-line:no-console
    }).catch((e) => console.log("Failed to load dependents.", e));
  }

  public render() {
    if (!this.state.data) {
      return (
        <div>
          <h3>Dependents</h3>

          <div>...</div>
        </div>
      );
    }

    if (this.state.totalHits === 0) {
      return (
        <div>
          <h3>Dependents</h3>

          <div>No packages depend on {this.props.packageId}</div>
        </div>
      );
    }

    return (
        <div>
          <h3>Dependents</h3>

          <p>{this.state.totalHits} {this.state.totalHits === 1 ? 'package depends' : 'packages depend' } on {this.props.packageId}:</p>
          <div>
            <ul>
              {this.state.data.map(dependent => (
                <li key={dependent}>{dependent}</li>
              ))}
            </ul>
          </div>
        </div>
    );
  }
}

export default Dependents;
