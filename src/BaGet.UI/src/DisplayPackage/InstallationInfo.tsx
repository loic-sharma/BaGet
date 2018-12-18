import { Icon } from 'office-ui-fabric-react/lib/Icon';
import * as React from 'react';
import * as CopyToClipboard from 'react-copy-to-clipboard';

import './InstallationInfo.css';

interface IInstallationInfoProps {
  id: string;
  version: string;
}

interface IInstallationInfoState {
  selected: Tab;
  prefix: string;
  content: string;
}

enum Tab {
  DotNet,
  Paket,
  PackageManager,
}

class InstallationInfo extends React.Component<IInstallationInfoProps, IInstallationInfoState> {

  constructor(props: IInstallationInfoProps) {
    super(props);

    this.state = this.buildState(Tab.DotNet);
  }

  public render() {
    return (
      <div className="installation-info">
        <ul className="nav">
          <InstallationInfoTab type={Tab.DotNet} selected={this.state.selected} onSelect={this.handleSelect} />
          <InstallationInfoTab type={Tab.Paket} selected={this.state.selected} onSelect={this.handleSelect} />
          <InstallationInfoTab type={Tab.PackageManager} selected={this.state.selected} onSelect={this.handleSelect} />
        </ul>

        <div className="content">
          <div className="script">
            {this.state.prefix} {this.state.content}
          </div>
          <div className="copy-button">
            <CopyToClipboard text={this.state.content}>
              <button className="btn btn-default btn-warning" type="button" data-tottle="popover" data-placement="bottom" data-content="Copied">
                <Icon iconName="Copy" className="ms-Icon" />
              </button>
            </CopyToClipboard>
          </div>
        </div>
      </div>
    );
  }

  private handleSelect = (selected: Tab) =>
    this.setState(this.buildState(selected));

  private buildState(tab: Tab): IInstallationInfoState {
    let content: string;
    let prefix: string;

    switch (tab) {
      case Tab.DotNet:
        content = `dotnet add package ${this.props.id} --version ${this.props.version}`;
        prefix = ">";
        break;

      case Tab.Paket:
        content = `paket add ${this.props.id} --version ${this.props.version}`;
        prefix = ">";
        break;

      default:
      case Tab.PackageManager:
        content = `Install-Package ${this.props.id} -Version ${this.props.version}`;
        prefix = "PM>";
        break;
    }

    return {
      content,
      prefix,
      selected: tab,
    };
  }
}

interface IInstallationInfoTabProps {
  type: Tab;
  selected: Tab;
  onSelect(value: Tab): void;
}

// tslint:disable-next-line:max-classes-per-file
class InstallationInfoTab extends React.Component<IInstallationInfoTabProps> {

  private title: string;

  constructor(props: IInstallationInfoTabProps) {
    super(props);

    switch (props.type) {
      case Tab.DotNet: this.title = ".NET CLI"; break;
      case Tab.Paket: this.title = "Paket CLI"; break;
      case Tab.PackageManager: this.title = "Package Manager"; break;
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

export default InstallationInfo;
