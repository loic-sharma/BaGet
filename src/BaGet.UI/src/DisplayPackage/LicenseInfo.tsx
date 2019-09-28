import { Icon } from 'office-ui-fabric-react/lib/Icon';
import * as React from 'react';

interface ILicenseInfoProps {
  url: string;
}

class LicenseInfo extends React.Component<ILicenseInfoProps> {

  public render() {
    if (!this.props.url) {
      return null;
    }

    return (
        <li>
            <Icon iconName="Certificate" className="ms-Icon" />
            <a href={this.props.url}>License</a>
        </li>
    );
  }
}

export default LicenseInfo;
