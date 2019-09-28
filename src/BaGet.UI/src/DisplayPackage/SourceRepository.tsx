import * as React from 'react';
import './SourceRepository.css';

interface ISourceRepositoryProps {
  url: string;
  type: string;
}

class SourceRepository extends React.Component<ISourceRepositoryProps> {

  public render() {
    if (!this.props.url) {
        return null;
    }

    // TODO: Add an icon URL base off the repository type.
    return (
        <li>
          <img className="icon" aria-hidden="true" alt="GitHub logo" src="https://www.nuget.org/Content/gallery/img/github-32x32.png" />
          <a href={this.props.url}>Source code</a>
        </li>
    );
  }
}

export default SourceRepository;
