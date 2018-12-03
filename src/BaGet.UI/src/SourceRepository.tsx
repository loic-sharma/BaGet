import * as React from 'react';

interface ISourceRepositoryProps {
  url: string;
  type: string;
}

class SourceRepository extends React.Component<ISourceRepositoryProps> {

  constructor(props: ISourceRepositoryProps) {
    super(props);

    this.state = {items: []};
  }

  public render() {
    if (!this.props.url) {
        return null;
    }

    // TODO: Add an icon URL base off the repository type.
    return (
        <div>
            <a href={this.props.url}>Source Code</a>
        </div>
    );
  }
}

export default SourceRepository;