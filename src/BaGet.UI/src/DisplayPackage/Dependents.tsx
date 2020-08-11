import { Icon } from 'office-ui-fabric-react/lib/Icon';
import { config } from '../config';
import * as React from 'react';

import './Dependents.css'

interface IDependentsProps {
  packageId: string;
}

interface IDependentsState {
  totalHits: number | undefined;
  data: IDependentState[] | undefined;
}

interface IDependentState {
  id: string | undefined;
  key: number | undefined;
  description: string | undefined;
  totalDownloads: number | undefined;
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
      this.setState(json as IDependentsState);
    // tslint:disable-next-line:no-console
    }).catch((e) => console.log("Failed to load dependents.", e));
  }

  public render() {
    if (!this.state.data) {
      return (
        <div>...</div>
      );
    }

    if (this.state.totalHits === 0) {
      return (
        <div>No packages depend on {this.props.packageId}.</div>
      );
    }

    return (
        <div>
          <p>{this.state.totalHits} {this.state.totalHits === 1 ? 'package depends' : 'packages depend' } on {this.props.packageId}:</p>
          <div>
            <table>
              <thead>
                <tr>
                  <th className="col-sm-10">Package</th>
                  <th className="col-sm-2">Downloads</th>
                </tr>
              </thead>
              <tbody>
                {this.state.data.map(dependent => (
                  <tr key={dependent.id}>
                    <td>
                      <a href={config.apiUrl + "/packages/" + dependent.id}>{dependent.id}</a>
                      <div>{dependent.description}</div>
                    </td>
                    <td>
                      <Icon iconName="Download" className="ms-Icon" />
                      <span>{dependent.totalDownloads?.toLocaleString()}</span>
                    </td>
                  </tr>

                ))}
              </tbody>
            </table>
          </div>
        </div>
    );
  }
}

export default Dependents;
