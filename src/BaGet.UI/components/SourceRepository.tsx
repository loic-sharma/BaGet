import * as React from 'react';

interface SourceRepositoryProps {
  url: string;
  type: string;
}

export default class SourceRepository extends React.Component<SourceRepositoryProps> {

  constructor(props: SourceRepositoryProps) {
    super(props);

    this.state = {items: []};
  }

  render() {
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