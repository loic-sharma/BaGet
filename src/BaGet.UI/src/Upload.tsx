import { Icon } from 'office-ui-fabric-react/lib/Icon';
import * as React from 'react';
import CopyText from 'copy-text-to-clipboard';

import './Upload.css';

interface IUploadState {
  selected: Tab;
  content: string[];
  documentationUrl: string;
  name: string;
}

enum Tab {
  DotNet,
  NuGet,
  Paket,
  PowerShellGet
}

class Upload extends React.Component<{}, IUploadState> {

  private baseUrl: string;
  private serviceIndexUrl: string;
  private publishUrl: string;

  constructor(props: {}) {
    super(props);

    const pathEnd = window.location.href.indexOf("/upload");

    this.baseUrl = window.location.href.substring(0, pathEnd);
    this.serviceIndexUrl = this.baseUrl + "/v3/index.json";
    this.publishUrl = this.baseUrl + "/api/v2/package";
    this.state = this.buildState(Tab.DotNet);
  }

  public render() {
    return (
      <div className="col-sm-12">
        <h1>Upload</h1>
        <hr className="breadcrumb-divider" />

        <div>You can push packages using the service index <code>{this.serviceIndexUrl}</code>.</div>

        <div className="upload-info">
          <ul className="nav">
            <UploadTab type={Tab.DotNet} selected={this.state.selected} onSelect={this.handleSelect} />
            <UploadTab type={Tab.NuGet} selected={this.state.selected} onSelect={this.handleSelect} />
            <UploadTab type={Tab.Paket} selected={this.state.selected} onSelect={this.handleSelect} />
            <UploadTab type={Tab.PowerShellGet} selected={this.state.selected} onSelect={this.handleSelect} />
          </ul>

          <div className="content">
            <div className="script">
              {this.state.content.map(value => (
                <div key={value}>
                  > {value}
                </div>
              ))}
            </div>
            <div className="copy-button">
              <button onClick={this.copyCommand} className="btn btn-default btn-warning" type="button" data-tottle="popover" data-placement="bottom" data-content="Copied">
                <Icon iconName="Copy" className="ms-Icon" />
              </button>
            </div>
          </div>
          <div className="icon-text alert alert-warning">
            For more information, please refer to <a target="_blank" rel="noopener noreferrer" href={this.state.documentationUrl}>{this.state.name}'s documentation</a>.
          </div>
        </div>
      </div>
    );
  }

  private handleSelect = (selected: Tab) =>
    this.setState(this.buildState(selected));

    private copyCommand = () =>
      CopyText(this.state.content.join("\n"));

  private buildState(tab: Tab): IUploadState {
    let name: string;
    let content: string[];
    let documentationUrl: string;

    switch (tab) {
      case Tab.DotNet:
        name = ".NET CLI";
        content = [`dotnet nuget push -s ${this.serviceIndexUrl} package.nupkg`];
        documentationUrl = "https://docs.microsoft.com/en-us/dotnet/core/tools/dotnet-nuget-push";
        break;

      case Tab.NuGet:
        name = "NuGet";
        content = [`nuget push -Source ${this.serviceIndexUrl} package.nupkg`];
        documentationUrl = "https://docs.microsoft.com/en-us/nuget/tools/cli-ref-push";
        break;

      case Tab.Paket:
        name = "Paket";
        content = [`paket push --url ${this.baseUrl} package.nupkg`];
        documentationUrl = "https://fsprojects.github.io/Paket/paket-push.html";
        break;

      default:
      case Tab.PowerShellGet:
        name = "PowerShellGet";
        content = [
          `Register-PSRepository -Name "BaGet" -SourceLocation "${this.serviceIndexUrl}" -PublishLocation "${this.publishUrl}" -InstallationPolicy "Trusted"`,
          `Publish-Module -Name PS-Module -Repository BaGet`
        ];
        documentationUrl = "https://docs.microsoft.com/en-us/powershell/module/powershellget/publish-module";
        break;
    }

    return {
      content,
      documentationUrl,
      name,
      selected: tab,
    };
  }
}

interface IUploadTabProps {
  type: Tab;
  selected: Tab;
  onSelect(value: Tab): void;
}

// tslint:disable-next-line:max-classes-per-file
class UploadTab extends React.Component<IUploadTabProps> {

  private title: string;

  constructor(props: IUploadTabProps) {
    super(props);

    switch (props.type) {
      case Tab.DotNet: this.title = ".NET CLI"; break;
      case Tab.NuGet: this.title = "NuGet CLI"; break;
      case Tab.Paket: this.title = "Paket CLI"; break;
      case Tab.PowerShellGet: this.title = "PowerShellGet"; break;
      default: this.title = "Unknown"; break;
    }
  }

  public render() {
    if (this.props.type === this.props.selected) {
      // eslint-disable-next-line
      return <li className="active"><a href="#">{this.title}</a></li>
    }

    // eslint-disable-next-line
    return <li><a href="#" onClick={this.onSelect}>{this.title}</a></li>
  }

  private onSelect = (e: React.MouseEvent<HTMLAnchorElement>) => this.props.onSelect(this.props.type);
}

export default Upload;
