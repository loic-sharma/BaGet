import * as React from 'react';

interface ILicenseInfoProps {
  url: string;
}

class LicenseInfo extends React.Component<ILicenseInfoProps> {

  constructor(props: ILicenseInfoProps) {
    super(props);

    this.state = {items: []};
  }

  public render() {
    if (!this.props.url) {
        return null;
    }

    return (
        <div>
            <a href={this.props.url}>License Info</a>
        </div>
    );
  }
}

export default LicenseInfo;