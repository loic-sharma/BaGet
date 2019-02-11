import { Icon } from 'office-ui-fabric-react/lib/Icon';
import * as React from 'react';
import * as CopyToClipboard from 'react-copy-to-clipboard';

import './Upload.css';

interface IUploadState {
  selected: Tab;
  content: string;
  documentationUrl: string;
  name: string;
}

enum Tab {
  DotNet,
  NuGet,
  Paket,
}

class Upload extends React.Component<{}, IUploadState> {

  private serviceIndexUrl: string;

  constructor(props: {}) {
    super(props);

    const pathEnd = window.location.href.indexOf("/upload");

    this.serviceIndexUrl = window.location.href.substring(0, pathEnd) + "/v3/index.json";
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
          </ul>

          <div className="content">
            <div className="script">
              > {this.state.content}
            </div>
            <div className="copy-button">
              <CopyToClipboard text={this.state.content}>
                <button className="btn btn-default btn-warning" type="button" data-tottle="popover" data-placement="bottom" data-content="Copied">
                  <Icon iconName="Copy" className="ms-Icon" />
                </button>
              </CopyToClipboard>
            </div>
          </div>
          <div className="icon-text alert alert-warning">
            For more information, please refer to <a href={this.state.documentationUrl}>{this.state.name}'s documentation</a>.
          </div>
        </div>
      </div>
    );
  }

  private handleSelect = (selected: Tab) =>
    this.setState(this.buildState(selected));

  private buildState(tab: Tab): IUploadState {
    let name: string;
    let content: string;
    let documentationUrl: string;

    switch (tab) {
      case Tab.DotNet:
        name = ".NET CLI";
        content = `dotnet nuget push -s ${this.serviceIndexUrl} package.nupkg`;
        documentationUrl = "https://docs.microsoft.com/en-us/dotnet/core/tools/dotnet-nuget-push";
        break;

      case Tab.NuGet:
        name = "NuGet";
        content = `nuget -Source ${this.serviceIndexUrl} package.nupkg`;
        documentationUrl = "https://docs.microsoft.com/en-us/nuget/tools/cli-ref-push";
        break;

      default:
      case Tab.Paket:
        name = "Paket";
        content = `paket push --url ${this.serviceIndexUrl} package.nupkg`;
        documentationUrl = "https://fsprojects.github.io/Paket/paket-push.html";
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
    }
  }

  public render() {
    if (this.props.type === this.props.selected) {
      return <li className="active"><a href="#">{this.title}</a></li>
    }

    return <li><a href="#" onClick={this.onSelect}>{this.title}</a></li>
  }

  private onSelect = (e: React.MouseEvent<HTMLAnchorElement>) => this.props.onSelect(this.props.type);
}

export default Upload;
