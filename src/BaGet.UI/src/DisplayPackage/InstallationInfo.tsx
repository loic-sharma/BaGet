import { Icon } from 'office-ui-fabric-react/lib/Icon';
import * as React from 'react';
import CopyText from 'copy-text-to-clipboard';

import './InstallationInfo.css';

export enum PackageType {
  Dependency,
  DotnetTool,
  DotnetTemplate
}

interface IInstallationInfoProps {
  id: string;
  version: string;
  packageType: PackageType;
}

interface IInstallationInfoState {
  selected: Tab;
  prefix: string;
  content: string;
}

enum Tab {
  Dotnet,
  DotnetTool,
  DotnetTemplate,
  PackageReference,
  Paket,
  PackageManager,
}

export class InstallationInfo extends React.Component<IInstallationInfoProps, IInstallationInfoState> {

  constructor(props: IInstallationInfoProps) {
    super(props);

    switch (props.packageType) {
      case PackageType.Dependency:
        this.state = this.buildState(Tab.Dotnet);
        break;

      case PackageType.DotnetTool:
        this.state = this.buildState(Tab.DotnetTool);
        break;

      case PackageType.DotnetTemplate:
        this.state = this.buildState(Tab.DotnetTemplate);
        break;
    }
  }

  public render() {
    return (
      <div className="installation-info">
        <ul className="nav">
          {(() =>
          {
            switch (this.props.packageType) {
              case PackageType.Dependency:
                return (
                  <ul className="nav">
                    <InstallationInfoTab
                      type={Tab.Dotnet}
                      selected={this.state.selected}
                      onSelect={this.handleSelect} />
                    <InstallationInfoTab
                      type={Tab.PackageReference}
                      selected={this.state.selected}
                      onSelect={this.handleSelect} />
                    <InstallationInfoTab
                      type={Tab.Paket}
                      selected={this.state.selected}
                      onSelect={this.handleSelect} />
                    <InstallationInfoTab
                      type={Tab.PackageManager}
                      selected={this.state.selected}
                      onSelect={this.handleSelect} />
                  </ul>
                );

              case PackageType.DotnetTool:
                return (
                  <ul className="nav">
                    <InstallationInfoTab
                      type={Tab.DotnetTool}
                      selected={this.state.selected}
                      onSelect={this.handleSelect} />
                  </ul>
                );

              case PackageType.DotnetTemplate:
                return (
                  <ul className="nav">
                    <InstallationInfoTab
                      type={Tab.DotnetTemplate}
                      selected={this.state.selected}
                      onSelect={this.handleSelect} />
                  </ul>
                );
            }
          })()}
        </ul>

        <div className="content">
          <div className="script">
            {this.state.prefix} {this.state.content}
          </div>
          <div className="copy-button">
            <button onClick={this.copyCommand} className="btn btn-default btn-warning" type="button" data-tottle="popover" data-placement="bottom" data-content="Copied">
              <Icon iconName="Copy" className="ms-Icon" />
            </button>
          </div>
        </div>
      </div>
    );
  }

  private handleSelect = (selected: Tab) =>
    this.setState(this.buildState(selected));

  private copyCommand = () =>
    CopyText(this.state.content);

  private buildState(tab: Tab): IInstallationInfoState {
    let content: string;
    let prefix: string;

    switch (tab) {
      case Tab.Dotnet:
        content = `dotnet add package ${this.props.id} --version ${this.props.version}`;
        prefix = ">";
        break;

      case Tab.DotnetTool:
        content = `dotnet tool install --global ${this.props.id} --version ${this.props.version}`;
        prefix = ">";
        break;

      case Tab.DotnetTemplate:
        content = `dotnet new --install ${this.props.id}::${this.props.version}`;
        prefix = ">";
        break;

      case Tab.PackageReference:
        content = `<PackageReference Include="${this.props.id}" Version="${this.props.version}" />`;
        prefix = "";
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
      case Tab.Dotnet: this.title = ".NET CLI"; break;
      case Tab.DotnetTool: this.title = ".NET CLI"; break;
      case Tab.DotnetTemplate: this.title = ".NET CLI"; break;
      case Tab.PackageReference: this.title = "PackageReference"; break;
      case Tab.Paket: this.title = "Paket CLI"; break;
      case Tab.PackageManager: this.title = "Package Manager"; break;
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

  private onSelect = () => this.props.onSelect(this.props.type);
}

export default InstallationInfo;
